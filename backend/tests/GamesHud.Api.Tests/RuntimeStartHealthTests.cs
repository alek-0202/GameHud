using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using Microsoft.Extensions.Options;
using GamesHud.Api.Persistence.ManagedServers;

namespace GamesHud.Api.Tests;

public sealed class RuntimeStartHealthTests
{
    [Fact]
    public void ManagedDockerBoundaryExposesOnlyExistingCreateAndNewStartMutation()
    {
        var methods = typeof(IDockerManagedRuntimeClient).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        Assert.Equal(["CreateAsync", "ImageExistsAsync", "InspectAsync", "ListAsync", "StartAsync"], methods);
        Assert.DoesNotContain(methods, name => name.Contains("Stop", StringComparison.Ordinal)
            || name.Contains("Restart", StringComparison.Ordinal) || name.Contains("Remove", StringComparison.Ordinal)
            || name.Contains("Kill", StringComparison.Ordinal) || name.Contains("Exec", StringComparison.Ordinal)
            || name.Contains("Pull", StringComparison.Ordinal) || name.Contains("Network", StringComparison.Ordinal)
            || name.Contains("Volume", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoppedManagedRuntimeStartsOnceAndIsConfirmedRunning()
    {
        var client = new FakeClient(Inspection("created"), Inspection("running"));
        var outcome = await Adapter(client).StartAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, outcome.Status);
        Assert.Equal(1, client.StartCalls);
    }

    [Fact]
    public async Task AlreadyRunningRuntimeIsIdempotent()
    {
        var client = new FakeClient(Inspection("running"));
        var outcome = await Adapter(client).StartAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, outcome.Status);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task ForeignNameCollisionNeverStarts()
    {
        var client = new FakeClient(Inspection("created")) { ForeignLabels = true };
        var outcome = await Adapter(client).StartAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.KnownFailure, outcome.Status);
        Assert.Equal(DockerRuntimeErrorCodes.StartConflict, outcome.SafeCode);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task UnmanagedOrLegacyRuntimeNeverReachesStartProvider()
    {
        var executor = new NeverExecutor();
        var step = new StartRuntimeProvisioningStep(new NullBuilder(), new AllowPolicy(), new FakePaths(), executor);
        var result = await step.ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(ProvisioningStepResultStatuses.Failed, result.Status);
        Assert.Equal(0, executor.StartCalls);
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("restarting")]
    [InlineData("dead")]
    [InlineData("removing")]
    public async Task UnsafeRuntimeStateNeverStarts(string state)
    {
        var client = new FakeClient(Inspection(state));
        var outcome = await Adapter(client).StartAsync(Context(), CancellationToken.None);
        Assert.Equal(DockerRuntimeErrorCodes.StartConflict, outcome.SafeCode);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task LostResponseAfterStartIsUnknownAndNotRetried()
    {
        var client = new FakeClient(Inspection("created")) { StartException = new IOException("lost") };
        var outcome = await Adapter(client).StartAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Unknown, outcome.Status);
        Assert.Equal(1, client.StartCalls);
    }

    [Theory]
    [InlineData(ManagedRuntimeStates.Running, ProvisioningReconciliationOutcomes.EffectExists)]
    [InlineData(ManagedRuntimeStates.Created, ProvisioningReconciliationOutcomes.EffectAbsent)]
    [InlineData(ManagedRuntimeStates.Exited, ProvisioningReconciliationOutcomes.EffectAbsent)]
    [InlineData(ManagedRuntimeStates.Restarting, ProvisioningReconciliationOutcomes.Ambiguous)]
    [InlineData(ManagedRuntimeStates.Paused, ProvisioningReconciliationOutcomes.Ambiguous)]
    [InlineData(ManagedRuntimeStates.Dead, ProvisioningReconciliationOutcomes.Ambiguous)]
    public async Task StartReconciliationClassifiesCurrentState(string state, string expected)
    {
        var reconciler = Reconciler(new FakeInspector(new ManagedRuntimeInspection(state, "container-1")));
        var result = await reconciler.InspectAsync(Operation(), Step(), CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task StartReconciliationTreatsProviderFailureAsAmbiguous()
    {
        var reconciler = Reconciler(new FakeInspector(exception: new IOException("provider unavailable")));
        var result = await reconciler.InspectAsync(Operation(), Step(), CancellationToken.None);
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, result.Outcome);
    }

    [Fact]
    public async Task CancellationBeforeStartDispatchCallsNoStart()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new FakeClient(Inspection("created"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Adapter(client).StartAsync(Context(), cancellation.Token));
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task RuntimeWithoutDockerHealthcheckIsReadyWhenRunning()
    {
        var result = await HealthStep(new FakeClient(Inspection("running"))).ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
    }

    [Fact]
    public async Task DockerHealthStartingThenHealthyUsesBoundedPolling()
    {
        var delay = new FakeDelay();
        var result = await HealthStep(new FakeClient(Inspection("running", "starting"), Inspection("running", "healthy")), delay)
            .ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(ProvisioningStepResultStatuses.Succeeded, result.Status);
        Assert.Equal(1, delay.Calls);
    }

    [Fact]
    public async Task DockerUnhealthyFailsWithoutLifecycleMutation()
    {
        var client = new FakeClient(Inspection("running", "unhealthy"));
        var result = await HealthStep(client).ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(ProvisioningStepResultStatuses.Failed, result.Status);
        Assert.Equal(RuntimeStartHealthErrorCodes.Unhealthy, result.ErrorCode);
        Assert.Equal(0, client.StartCalls);
    }

    [Theory]
    [InlineData("created", RuntimeStartHealthErrorCodes.HealthTimeout)]
    [InlineData("restarting", RuntimeStartHealthErrorCodes.HealthTimeout)]
    [InlineData("dead", RuntimeStartHealthErrorCodes.Unhealthy)]
    public async Task NonReadyRuntimeStatesFailSafely(string state, string expectedCode)
    {
        var client = new FakeClient(Inspection(state));
        var result = await HealthStep(client, timeout: 1, interval: 1)
            .ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task StartingUntilTimeoutFailsSafelyAndDoesNotStart()
    {
        var client = new FakeClient(Inspection("running", "starting"));
        var delay = new FakeDelay();
        var result = await HealthStep(client, delay, timeout: 2, interval: 1)
            .ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(RuntimeStartHealthErrorCodes.HealthTimeout, result.ErrorCode);
        Assert.Equal(2, delay.Calls);
        Assert.Equal(0, client.StartCalls);
    }

    [Fact]
    public async Task StartSuccessRemainsDistinctFromSubsequentHealthFailure()
    {
        var startClient = new FakeClient(Inspection("created"), Inspection("running"));
        var start = await Adapter(startClient).StartAsync(Context(), CancellationToken.None);
        var healthClient = new FakeClient(Inspection("running", "unhealthy"));
        var health = await HealthStep(healthClient).ExecuteAsync(ProvisioningContext(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, start.Status);
        Assert.Equal(ProvisioningStepResultStatuses.Failed, health.Status);
        Assert.Equal(1, startClient.StartCalls);
        Assert.Equal(0, healthClient.StartCalls);
    }

    private static VerifyRuntimeHealthProvisioningStep HealthStep(FakeClient client, FakeDelay? delay = null,
        int timeout = 4, int interval = 1)
    {
        var specification = Specification();
        return new(new FakeBuilder(specification), new AllowPolicy(), new FakePaths(), Adapter(client), delay ?? new FakeDelay(),
            Options.Create(new RuntimeHealthOptions { TimeoutSeconds = timeout, PollIntervalSeconds = interval }));
    }

    private static DockerGameRuntimeAdapter Adapter(FakeClient client) => new(client, new SafeStorage());

    private static StartRuntimeReconciler Reconciler(IManagedRuntimeInspector inspector) => new(
        new FakeReconciliationBuilder(), new AllowPolicy(), new FakePaths(), inspector);

    private static RuntimeMutationExecutionContext Context() => new(
        new ValidatedRuntimeMutationSpecification(Specification()), RuntimeMutationKind.StartRuntime,
        ProvisioningStepIds.StartRuntime, 1);

    private static RuntimeMutationSpecification Specification() => new(
        new GameServerId("server-1"), new GameId("palworld"), "operation-1", "docker",
        new TrustedRuntimeImage("docker", "trusted/game", "1", "test"),
        [new("port-1", "game", PortProtocols.Udp, 7000, PortExposures.Public)],
        [new("storage-1", "data", "C:\\managed\\server-1", "/palworld", false)], [],
        new(2, 1024), RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged);

    private static ProvisioningContext ProvisioningContext()
    {
        var plan = new ValidatedProvisioningPlan(new GameServerId("server-1"), new GameId("palworld"), "Server",
            "docker", "compatible", [], [], [], [], ProvisioningStepIds.All);
        return new("operation-1", new PalworldGameDefinition(), plan,
            new ManagedServerReservationResult("server-1", "operation-1", [], []));
    }

    private static ProvisioningOperationSnapshot Operation() => new(
        "operation-1", "server-1", "running", ProvisioningStepIds.StartRuntime, null, null,
        DateTimeOffset.UtcNow, null, ProvisioningPipeline.Version, 1, true, [Step()]);

    private static ProvisioningStepSnapshot Step() => new(
        ProvisioningStepIds.StartRuntime, 7, "running", 1,
        ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 1,
        DateTimeOffset.UtcNow, null, null, null, null, null, null);

    private static ContainerInspectResponse Inspection(string state, string? health = null) => new()
    {
        Config = new Config { Image = "trusted/game:1" },
        State = new ContainerState
        {
            Status = state,
            Running = state == "running",
            Paused = state == "paused",
            Restarting = state == "restarting",
            Dead = state == "dead",
            Health = health is null ? null : new Health { Status = health }
        },
        HostConfig = DockerCreateContainerMapper.Map(Context()).HostConfig
    };

    private sealed class FakeClient(params ContainerInspectResponse[] inspections) : IDockerManagedRuntimeClient
    {
        private readonly Queue<ContainerInspectResponse> _inspections = new(inspections);
        private ContainerInspectResponse? _last;
        public bool ForeignLabels { get; init; }
        public Exception? StartException { get; init; }
        public int StartCalls { get; private set; }
        public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<CreateContainerResponse> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Create is forbidden in GH-13 tests.");
        public Task<bool> StartAsync(string id, CancellationToken cancellationToken)
        {
            StartCalls++;
            return StartException is null ? Task.FromResult(true) : Task.FromException<bool>(StartException);
        }
        public Task<IReadOnlyCollection<ContainerListResponse>> ListAsync(CancellationToken cancellationToken)
        {
            var expected = DockerCreateContainerMapper.Map(Context());
            var labels = ForeignLabels ? new Dictionary<string, string>() : new Dictionary<string, string>(expected.Labels);
            return Task.FromResult<IReadOnlyCollection<ContainerListResponse>>([new()
            {
                ID = "container-1", Names = ["/" + expected.Name], Image = expected.Image, Labels = labels
            }]);
        }
        public Task<ContainerInspectResponse> InspectAsync(string id, CancellationToken cancellationToken)
        {
            if (_inspections.Count > 0) _last = _inspections.Dequeue();
            return Task.FromResult(_last!);
        }
    }

    private sealed class SafeStorage : IManagedRuntimeStorageValidator
    {
        public bool IsPreparedAndSafe(IReadOnlyCollection<RuntimeStorageMount> mounts) => true;
    }

    private sealed class FakeDelay : IRuntimeHealthDelay
    {
        public int Calls { get; private set; }
        public Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }

    private sealed class FakeBuilder(RuntimeMutationSpecification specification) : IRuntimeSpecificationBuilder
    {
        public Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeMutationSpecification?>(specification);
    }

    private sealed class NullBuilder : IRuntimeSpecificationBuilder
    {
        public Task<RuntimeMutationSpecification?> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeMutationSpecification?>(null);
    }

    private sealed class FakeReconciliationBuilder : IRuntimeReconciliationSpecificationBuilder
    {
        public Task<(RuntimeMutationSpecification? Specification, GameDefinition? Definition)> BuildForReconciliationAsync(
            string operationId, GameServerId gameServerId, CancellationToken cancellationToken) =>
            Task.FromResult<(RuntimeMutationSpecification?, GameDefinition?)>((Specification(), new PalworldGameDefinition()));
    }

    private sealed class FakeInspector(ManagedRuntimeInspection? inspection = null, Exception? exception = null) : IManagedRuntimeInspector
    {
        public Task<ManagedRuntimeInspection> InspectManagedAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken) =>
            exception is null ? Task.FromResult(inspection!) : Task.FromException<ManagedRuntimeInspection>(exception);
    }

    private sealed class NeverExecutor : IRuntimeMutationExecutor
    {
        public int StartCalls { get; private set; }
        public Task<RuntimeMutationExecutionResult> ExecuteCreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
        public Task<RuntimeMutationExecutionResult> ExecuteStartAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
        {
            StartCalls++;
            throw new InvalidOperationException("Start provider must not be reached.");
        }
    }

    private sealed class AllowPolicy : IRuntimeMutationPolicy
    {
        public RuntimeMutationPolicyResult Validate(RuntimeMutationSpecification specification, GameDefinition definition, string managedDataRoot) =>
            new(true, new ValidatedRuntimeMutationSpecification(specification), []);
    }

    private sealed class FakePaths : IManagedStoragePathBuilder
    {
        public ManagedStorageLayout CreateLayout(GameServerId gameServerId) => new("C:\\managed", "C:\\managed\\server-1", "servers/server-1");
    }
}
