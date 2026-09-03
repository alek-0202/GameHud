using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.ManagedServers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesHud.Api.Tests;

public sealed class RuntimeMutationExecutorTests
{
    [Fact]
    public async Task SuccessfulProviderOutcomeBecomesSuccess()
    {
        var adapter = new FakeAdapter((_, _) => Task.FromResult(RuntimeProviderOutcome.Success("safe success")));
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, result.Status);
        Assert.False(result.ReconciliationRequired);
        Assert.True(result.ProviderInvoked);
    }

    [Fact]
    public async Task StartUsesTypedExecutorPath()
    {
        RuntimeMutationKind observed = default;
        var adapter = new FakeAdapter((context, _) =>
        {
            observed = context.MutationKind;
            return Task.FromResult(RuntimeProviderOutcome.Success("started"));
        });
        var create = CreateContext();
        var start = new RuntimeMutationExecutionContext(create.Specification, RuntimeMutationKind.StartRuntime,
            ProvisioningStepIds.StartRuntime, 1);
        var result = await CreateExecutor(adapter).ExecuteStartAsync(start, CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, result.Status);
        Assert.Equal(RuntimeMutationKind.StartRuntime, observed);
    }

    [Fact]
    public async Task KnownProviderFailureBecomesSafeRetryableFailure()
    {
        var adapter = new FakeAdapter((_, _) => Task.FromResult(RuntimeProviderOutcome.KnownFailure("provider_rejected", "Safe failure.")));
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.KnownFailure, result.Status);
        Assert.Equal(ProvisioningRetryClassifications.SafeToRetry, result.RetryClassification);
        Assert.False(result.ReconciliationRequired);
    }

    [Fact]
    public async Task UnknownProviderOutcomeRequiresReconciliation()
    {
        var adapter = new FakeAdapter((_, _) => Task.FromResult(RuntimeProviderOutcome.Unknown("provider_unknown", "Outcome is unknown.")));
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Unknown, result.Status);
        Assert.Equal(ProvisioningRetryClassifications.RequiresInspection, result.RetryClassification);
        Assert.True(result.ReconciliationRequired);
    }

    [Fact]
    public async Task ProviderExceptionIsTranslatedWithoutRawMessage()
    {
        const string sentinel = "test-secret-never-leak";
        var adapter = new FakeAdapter((_, _) => throw new InvalidOperationException(sentinel));
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Unknown, result.Status);
        Assert.True(result.ReconciliationRequired);
        Assert.DoesNotContain(sentinel, result.SafeMessage);
        Assert.DoesNotContain(sentinel, result.SafeCode);
    }

    [Fact]
    public async Task CancellationBeforeInvocationDoesNotCallProvider()
    {
        var adapter = new FakeAdapter((_, _) => Task.FromResult(RuntimeProviderOutcome.Success("unused")));
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), source.Token);
        Assert.Equal(RuntimeMutationOutcomeStatuses.CancelledBeforeInvocation, result.Status);
        Assert.False(result.ProviderInvoked);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task CancellationAfterProviderInvocationIsUnknown()
    {
        var adapter = new FakeAdapter((_, token) => throw new OperationCanceledException(token));
        var result = await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Unknown, result.Status);
        Assert.True(result.ProviderInvoked);
        Assert.True(result.ReconciliationRequired);
    }

    [Fact]
    public async Task CancellationTokenIsPropagated()
    {
        CancellationToken observed = default;
        var adapter = new FakeAdapter((_, token) =>
        {
            observed = token;
            return Task.FromResult(RuntimeProviderOutcome.Success("ok"));
        });
        using var source = new CancellationTokenSource();
        await CreateExecutor(adapter).ExecuteCreateAsync(CreateContext(), source.Token);
        Assert.Equal(source.Token, observed);
    }

    [Fact]
    public void MutationIdentityIsStableAcrossAttemptsAndUniqueAcrossServers()
    {
        var first = CreateContext("server-one", 1);
        var retry = CreateContext("server-one", 2);
        var other = CreateContext("server-two", 1);
        Assert.Equal(first.MutationExecutionId, retry.MutationExecutionId);
        Assert.NotEqual(first.ProviderResourceIdentity, other.ProviderResourceIdentity);
    }

    [Fact]
    public void AdapterContractAcceptsOnlyTypedExecutionContext()
    {
        var methods = typeof(IGameRuntimeAdapter).GetMethods();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, method =>
        {
            Assert.Equal(typeof(RuntimeMutationExecutionContext), method.GetParameters()[0].ParameterType);
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.ParameterType == typeof(string) || parameter.ParameterType.IsGenericType);
        });
    }

    [Fact]
    public async Task PolicyDenialNeverReachesExecutorOrProvider()
    {
        var specification = CreateUnvalidatedSpecification("server-one");
        var executor = new FakeExecutor();
        var step = new CreateRuntimeProvisioningStep(
            new FakeBuilder(specification), new DenyPolicy(), executor, new FakePaths(),
            NullLogger<CreateRuntimeProvisioningStep>.Instance);

        var result = await step.ExecuteAsync(CreateProvisioningContext(), CancellationToken.None);

        Assert.Equal(ProvisioningStepResultStatuses.Failed, result.Status);
        Assert.Equal(RuntimePolicyErrorCodes.RuntimePolicyDenied, result.ErrorCode);
        Assert.Equal(0, executor.CallCount);
    }

    private static RuntimeMutationExecutor CreateExecutor(IGameRuntimeAdapter adapter) =>
        new(adapter, NullLogger<RuntimeMutationExecutor>.Instance);

    private static RuntimeMutationExecutionContext CreateContext(string serverId = "server-one", int attempt = 1)
    {
        var definition = new PalworldGameDefinition();
        var root = Path.Combine(Path.GetTempPath(), "gameshud-gh10");
        var specification = CreateUnvalidatedSpecification(serverId);
        var validated = new RuntimeMutationPolicy().Validate(specification, definition, root).Specification!;
        return new(validated, RuntimeMutationKind.CreateRuntime, ProvisioningStepIds.CreateRuntime, attempt);
    }

    private static RuntimeMutationSpecification CreateUnvalidatedSpecification(string serverId)
    {
        var definition = new PalworldGameDefinition();
        var root = Path.Combine(Path.GetTempPath(), "gameshud-gh10");
        return new RuntimeMutationSpecification(new GameServerId(serverId), new GameId("palworld"),
            "operation-one", "docker", definition.RuntimeImages.Single(), [],
            [new("storage", "data", Path.Combine(root, "servers", serverId, "data"), "/palworld", false)],
            [], new(1, 1024), RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged);
    }

    private static ProvisioningContext CreateProvisioningContext()
    {
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-one"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [], [], ProvisioningStepIds.All);
        var reservation = new ManagedServerReservationResult("server-one", "operation-one", [], []);
        return new ProvisioningContext("operation-one", new PalworldGameDefinition(), plan, reservation);
    }

    private sealed class FakeAdapter(
        Func<RuntimeMutationExecutionContext, CancellationToken, Task<RuntimeProviderOutcome>> callback) : IGameRuntimeAdapter
    {
        public int CallCount { get; private set; }

        public Task<RuntimeProviderOutcome> CreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return callback(context, cancellationToken);
        }

        public Task<RuntimeProviderOutcome> StartAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return callback(context, cancellationToken);
        }
    }

    private sealed class FakeBuilder(RuntimeMutationSpecification specification) : IRuntimeSpecificationBuilder
    {
        public Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeMutationSpecification?>(specification);
    }

    private sealed class DenyPolicy : IRuntimeMutationPolicy
    {
        public RuntimeMutationPolicyResult Validate(RuntimeMutationSpecification specification, GameDefinition definition, string managedDataRoot) =>
            new(false, null, [new(RuntimePolicyErrorCodes.RuntimePolicyDenied, "Denied.")]);
    }

    private sealed class FakeExecutor : IRuntimeMutationExecutor
    {
        public int CallCount { get; private set; }
        public Task<RuntimeMutationExecutionResult> ExecuteCreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Executor must not be called.");
        }

        public Task<RuntimeMutationExecutionResult> ExecuteStartAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken) =>
            ExecuteCreateAsync(context, cancellationToken);
    }

    private sealed class FakePaths : IManagedStoragePathBuilder
    {
        public ManagedStorageLayout CreateLayout(GameServerId gameServerId)
        {
            var root = Path.Combine(Path.GetTempPath(), "gameshud-gh10");
            return new(root, Path.Combine(root, "servers", gameServerId.ToString()), Path.Combine("servers", gameServerId.ToString()));
        }
    }
}
