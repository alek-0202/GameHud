using System.Text.Json;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.HostCapabilities.Services;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using GamesHud.Api.Secrets.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class ProvisioningTests
{
    [Fact]
    public void RequestContractCannotSupplyHostOrRuntimeMutationInputs()
    {
        var properties = typeof(CreateGameServerProvisioningRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["GameServerId", "GameId", "DisplayName"], properties);
        Assert.DoesNotContain(properties, name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Mount", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Privileged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidatedPlanAllowsOnlyOpaqueSecretReferences()
    {
        var reference = new SecretReference(SecretId.New());
        var plan = CreateValidatedPlan("server-one", 8211, "servers/server-one/data") with
        {
            SecretReferences = [reference]
        };
        var propertyTypes = typeof(ValidatedProvisioningPlan)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.Equal(reference, Assert.Single(plan.SecretReferences));
        Assert.DoesNotContain(typeof(SecretValue), propertyTypes);
    }

    [Fact]
    public async Task PlanBuilderReusesRequirementsAndPlannersAndAcceptsHostWarnings()
    {
        var definition = new PalworldGameDefinition();
        var builder = CreatePlanBuilder(definition, CreateCompatibleHost(logicalProcessors: 2));

        var result = await builder.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "palworld", "Server One"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(GameCompatibilityStatuses.CompatibleWithWarnings, result.Plan!.HostCompatibilityStatus);
        Assert.NotEmpty(result.Plan.HostWarnings);
        Assert.NotEmpty(result.Plan.Ports);
        Assert.NotEmpty(result.Plan.Storage);
        Assert.All(result.Plan.Storage, item => Assert.StartsWith("servers/server-one/", item.RelativePath));
        Assert.Empty(result.Plan.SecretReferences);
        Assert.Equal(ProvisioningStepIds.All, result.Plan.RequiredSteps);
    }

    [Fact]
    public async Task PlanBuilderRejectsUnknownGameInvalidServerIdAndIncompatibleHost()
    {
        var definition = new PalworldGameDefinition();
        var compatible = CreatePlanBuilder(definition, CreateCompatibleHost());
        var incompatible = CreatePlanBuilder(definition, CreateCompatibleHost(architecture: "arm64"));

        var unknown = await compatible.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "unknown", "Server"),
            CancellationToken.None);
        var invalid = await compatible.BuildAsync(
            new CreateGameServerProvisioningRequest("../escape", "palworld", "Server"),
            CancellationToken.None);
        var hostFailure = await incompatible.BuildAsync(
            new CreateGameServerProvisioningRequest("server-one", "palworld", "Server"),
            CancellationToken.None);

        Assert.Equal(ProvisioningErrorCodes.GameNotFound, unknown.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.InvalidGameServerId, invalid.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.HostIncompatible, hostFailure.Failure!.Code);
    }

    [Fact]
    public async Task PreviewDoesNotReserveOrMutateHost()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        var preview = await harness.Service.PreviewAsync(CreateRequest("preview"), CancellationToken.None);

        Assert.True(preview.IsValid);
        Assert.False(await database.ManagedGameServers.AnyAsync());
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task SuccessfulFoundationPersistsReservationProgressAndTerminalStatus()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        var result = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var operation = await harness.Operations.GetAsync(result.OperationId!);
        var server = await harness.Store.GetManagedServerAsync("server-one");

        Assert.True(result.Succeeded);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation!.Status);
        Assert.Equal(ProvisioningStepIds.Complete, operation.CurrentStep);
        Assert.False(operation.IsActive);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Equal(ManagedGameServerLifecycleStates.PendingProvisioning, server!.LifecycleState);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "servers")));
    }

    [Fact]
    public async Task ReservationInitializesDurableVersionedPipelineInSequence()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));

        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);
        var steps = operation!.Steps.OrderBy(step => step.Sequence).ToArray();

        Assert.Equal(ProvisioningPipeline.Version, operation.PipelineVersion);
        Assert.Equal(1, operation.Version);
        Assert.Equal(ProvisioningStepIds.ReserveResources, operation.CurrentStep);
        Assert.Equal(ProvisioningStepIds.All, steps.Select(step => step.StepId));
        Assert.Equal(Enumerable.Range(1, ProvisioningStepIds.All.Count), steps.Select(step => step.Sequence));
        Assert.All(steps.Take(3), step =>
        {
            Assert.Equal(ProvisioningStepStatuses.Succeeded, step.Status);
            Assert.Equal(1, step.Attempt);
            Assert.Equal(TimeSpan.Zero, step.StartedAtUtc!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, step.CompletedAtUtc!.Value.Offset);
        });
        Assert.All(steps.Skip(3), step =>
        {
            Assert.Equal(ProvisioningStepStatuses.Pending, step.Status);
            Assert.Equal(0, step.Attempt);
            Assert.Null(step.StartedAtUtc);
            Assert.Null(step.CompletedAtUtc);
        });
    }

    [Fact]
    public async Task DuplicateActiveAndCrossServerResourceConflictsAreSafe()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var harness = CreateHarness(database);

        await harness.Store.ReserveProvisioningPlanAsync(CreatePersistencePlan("active", 8301, "servers/active/data"));
        var active = await harness.Service.StartProvisioningAsync(CreateRequest("active"), CancellationToken.None);
        var first = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var duplicate = await harness.Service.StartProvisioningAsync(CreateRequest("server-one"), CancellationToken.None);
        var portConflictHarness = CreateHarness(database, _ => CreateValidatedPlan("server-two", 8211, "servers/server-two/data"));
        var portConflict = await portConflictHarness.Service.StartProvisioningAsync(CreateRequest("server-two"), CancellationToken.None);
        var storageConflictHarness = CreateHarness(database, _ => CreateValidatedPlan("server-three", 8403, "servers/server-one/data"));
        var storageConflict = await storageConflictHarness.Service.StartProvisioningAsync(CreateRequest("server-three"), CancellationToken.None);

        Assert.Equal(ProvisioningErrorCodes.OperationInProgress, active.Failure!.Code);
        Assert.True(first.Succeeded);
        Assert.Equal(ProvisioningErrorCodes.DuplicateServer, duplicate.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.PortConflict, portConflict.Failure!.Code);
        Assert.Equal(ProvisioningErrorCodes.StorageConflict, storageConflict.Failure!.Code);
    }

    [Fact]
    public async Task UnknownMutationFailurePersistsSafeErrorWithoutBlindCompensation()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var compensated = false;
        var steps = CreateSteps(
            new FakeStep(ProvisioningStepIds.PrepareStorage, ProvisioningStepResult.Success(), () => compensated = true),
            new FakeStep(ProvisioningStepIds.ConfigureGame, exception: new InvalidOperationException("TEST-ONLY-SECRET-MUST-NOT-PERSIST")));
        var engine = new ProvisioningEngine(operations, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);
        var recovery = await new ProvisioningRecoveryService(operations)
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);
        var databaseJson = JsonSerializer.Serialize(await database.ProvisioningOperations.AsNoTracking().ToArrayAsync());

        Assert.False(result.Succeeded);
        Assert.False(compensated);
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation!.Status);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, operation.CurrentStep);
        Assert.Equal(ProvisioningErrorCodes.StepFailed, operation.ErrorCode);
        Assert.True(operation.IsActive);
        Assert.Equal(ProvisioningRecoveryDecisions.Reconcile, recovery.Decision);
        Assert.DoesNotContain("TEST-ONLY-SECRET", databaseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StepObservesRunningCurrentStepAndTypedFailureStopsPipeline()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
        var observedRunning = false;
        var steps = CreateSteps(
            new ObservingStep(ProvisioningStepIds.PrepareStorage, async () =>
            {
                var running = await operations.GetAsync(reservation.ProvisioningOperationId);
                observedRunning = running!.Status == ProvisioningOperationStatuses.Running
                    && running.CurrentStep == ProvisioningStepIds.PrepareStorage;
                return ProvisioningStepResult.Success();
            }),
            new FakeStep(
                ProvisioningStepIds.ConfigureGame,
                ProvisioningStepResult.Failure("configuration_failed", "Configuration validation failed.")));
        var engine = new ProvisioningEngine(operations, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var failed = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.Equal(ProvisioningOperationStatuses.Pending, pending!.Status);
        Assert.True(observedRunning);
        Assert.False(result.Succeeded);
        Assert.Equal("configuration_failed", failed!.ErrorCode);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, failed.CurrentStep);
    }

    [Fact]
    public async Task CompensationProgressIsPersistedInReverseOrder()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var order = new List<string>();
        var steps = CreateSteps(
            new FakeStep(ProvisioningStepIds.PrepareStorage, compensation: () => order.Add(ProvisioningStepIds.PrepareStorage)),
            new FakeStep(ProvisioningStepIds.ConfigureGame, compensation: () => order.Add(ProvisioningStepIds.ConfigureGame)),
            new FakeStep(
                ProvisioningStepIds.CreateRuntime,
                ProvisioningStepResult.Failure("runtime_failed", "Runtime validation failed.")));
        var engine = new ProvisioningEngine(operations, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.False(result.Succeeded);
        Assert.Equal([ProvisioningStepIds.ConfigureGame, ProvisioningStepIds.PrepareStorage], order);
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation!.Status);
        Assert.False(operation.IsActive);
        Assert.Equal(ProvisioningStepStatuses.Compensated,
            operation.Steps.Single(step => step.StepId == ProvisioningStepIds.PrepareStorage).Status);
        Assert.Equal(ProvisioningStepStatuses.Compensated,
            operation.Steps.Single(step => step.StepId == ProvisioningStepIds.ConfigureGame).Status);
    }

    [Fact]
    public async Task CompensationFailureRemainsActiveAndRequiresManualIntervention()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var steps = CreateSteps(
            new FakeStep(ProvisioningStepIds.PrepareStorage, compensation: () => throw new InvalidOperationException("TEST-ONLY-COMPENSATION-SECRET")),
            new FakeStep(
                ProvisioningStepIds.ConfigureGame,
                ProvisioningStepResult.Failure("configuration_failed", "Configuration failed.")));
        var engine = new ProvisioningEngine(operations, steps, NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);
        var decision = await new ProvisioningRecoveryService(operations)
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);
        var json = JsonSerializer.Serialize(operation);

        Assert.Equal(ProvisioningOperationStatuses.CompensationFailed, result.Status);
        Assert.Equal(ProvisioningOperationStatuses.CompensationFailed, operation!.Status);
        Assert.True(operation.IsActive);
        Assert.Equal(ProvisioningStepStatuses.CompensationFailed,
            operation.Steps.Single(step => step.StepId == ProvisioningStepIds.PrepareStorage).Status);
        Assert.Equal(ProvisioningRecoveryDecisions.ManualIntervention, decision.Decision);
        Assert.DoesNotContain("TEST-ONLY-COMPENSATION-SECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationPersistsCancelledInsteadOfSucceeded()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var engine = new ProvisioningEngine(operations, CreateSteps(), NullLogger<ProvisioningEngine>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await engine.ExecuteAsync(CreateContext(reservation), cancellation.Token);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.False(result.Succeeded);
        Assert.Equal(ProvisioningErrorCodes.ProvisioningCancelled, operation!.ErrorCode);
        Assert.Equal(ProvisioningOperationStatuses.Cancelled, operation.Status);
        Assert.Equal(ProvisioningStepStatuses.Pending, operation.Steps.Single(step => step.StepId == ProvisioningStepIds.PrepareStorage).Status);
    }

    [Fact]
    public async Task CancellationDuringMutationRetainsActiveSlotForReconciliation()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var engine = new ProvisioningEngine(
            operations,
            CreateSteps(new FakeStep(ProvisioningStepIds.PrepareStorage, exception: new OperationCanceledException())),
            NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);
        var decision = await new ProvisioningRecoveryService(operations)
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);
        var step = operation!.Steps.Single(item => item.StepId == ProvisioningStepIds.PrepareStorage);

        Assert.Equal(ProvisioningOperationStatuses.Cancelled, result.Status);
        Assert.True(operation.IsActive);
        Assert.Equal(ProvisioningFailureTypes.Unknown, step.FailureType);
        Assert.Equal(ProvisioningRecoveryDecisions.Reconcile, decision.Decision);
    }

    [Fact]
    public async Task CancellationDuringReadOnlyStepIsTerminal()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var customSteps = ProvisioningPipeline.Steps.Select(step => new ProvisioningStepPlan(
            step.Id,
            step.Sequence,
            step.RetryClassification,
            step.SideEffectClassification,
            step.MaxAttempts,
            CompletedBeforeReservation: step.Sequence < 8)).ToArray();
        var reservation = await store.ReserveProvisioningPlanAsync(
            CreatePersistencePlan("server-one") with { Steps = customSteps });
        var operations = CreateOperationStore(database);
        var engine = new ProvisioningEngine(
            operations,
            CreateSteps(new FakeStep(ProvisioningStepIds.VerifyHealth, exception: new OperationCanceledException())),
            NullLogger<ProvisioningEngine>.Instance);

        var result = await engine.ExecuteAsync(CreateContext(reservation), CancellationToken.None);
        var operation = await operations.GetAsync(reservation.ProvisioningOperationId);
        var decision = await new ProvisioningRecoveryService(operations)
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);
        var step = operation!.Steps.Single(item => item.StepId == ProvisioningStepIds.VerifyHealth);

        Assert.Equal(ProvisioningOperationStatuses.Cancelled, result.Status);
        Assert.False(operation.IsActive);
        Assert.Equal(ProvisioningFailureTypes.Permanent, step.FailureType);
        Assert.Equal(ProvisioningRecoveryDecisions.Terminal, decision.Decision);
    }

    [Fact]
    public async Task InvalidTerminalTransitionIsRejected()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
        var running = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            reservation.ProvisioningOperationId,
            pending!.Version,
            ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepStatuses.Running));
        var succeeded = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            reservation.ProvisioningOperationId,
            running.Version,
            ProvisioningOperationStatuses.Succeeded,
            ProvisioningStepIds.Complete));

        await Assert.ThrowsAsync<ProvisioningTransitionException>(() => operations.ApplyCheckpointAsync(
            new ProvisioningCheckpoint(
                reservation.ProvisioningOperationId,
                succeeded.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.CreateRuntime)));
    }

    [Fact]
    public async Task IncompleteOperationQuerySurvivesNewDbContext()
    {
        using var root = TemporaryDirectory.Create();
        await using (var database = CreateInitializedDbContext(root.Path))
        {
            await CreateStore(database).ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        }

        await using var reloaded = CreateDbContext(root.Path);
        var incomplete = await CreateOperationStore(reloaded).GetIncompleteAsync();

        Assert.Single(incomplete);
        Assert.Equal(ProvisioningOperationStatuses.Pending, incomplete.Single().Status);
    }

    [Fact]
    public async Task RecoveryClassifiesCrashBeforeAndDuringMutationWithoutExecutingIt()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var recovery = new ProvisioningRecoveryService(operations);
        var before = await recovery.ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);
        var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
        await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            reservation.ProvisioningOperationId,
            pending!.Version,
            ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepStatuses.Running));

        await using var reloaded = CreateDbContext(root.Path);
        var during = await new ProvisioningRecoveryService(CreateOperationStore(reloaded))
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);

        Assert.Equal(ProvisioningRecoveryDecisions.Resume, before.Decision);
        Assert.Equal(ProvisioningStepIds.PrepareStorage, before.StepId);
        Assert.Equal(ProvisioningRecoveryDecisions.Reconcile, during.Decision);
        Assert.Equal(ProvisioningStepIds.PrepareStorage, during.StepId);
    }

    [Fact]
    public async Task RecoveryUsesPersistedNextStepAfterSuccessfulCheckpointAndRestart()
    {
        using var root = TemporaryDirectory.Create();
        await using (var database = CreateInitializedDbContext(root.Path))
        {
            var store = CreateStore(database);
            var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
            var operations = CreateOperationStore(database);
            var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
            var running = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                reservation.ProvisioningOperationId,
                pending!.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Running));
            await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                reservation.ProvisioningOperationId,
                running.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Succeeded));
        }

        await using var reloaded = CreateDbContext(root.Path);
        var operation = (await CreateOperationStore(reloaded).GetIncompleteAsync()).Single();
        var decision = await new ProvisioningRecoveryService(CreateOperationStore(reloaded))
            .ClassifyAsync(operation.OperationId, CancellationToken.None);

        Assert.Equal(ProvisioningStepStatuses.Succeeded,
            operation.Steps.Single(step => step.StepId == ProvisioningStepIds.PrepareStorage).Status);
        Assert.Equal(ProvisioningRecoveryDecisions.Resume, decision.Decision);
        Assert.Equal(ProvisioningStepIds.ConfigureGame, decision.StepId);
    }

    [Fact]
    public async Task RestartDuringCompensationRequiresManualIntervention()
    {
        using var root = TemporaryDirectory.Create();
        string operationId;
        await using (var database = CreateInitializedDbContext(root.Path))
        {
            var store = CreateStore(database);
            var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
            operationId = reservation.ProvisioningOperationId;
            var operations = CreateOperationStore(database);
            var pending = await operations.GetAsync(operationId);
            var running = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                operationId,
                pending!.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Running));
            var succeeded = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                operationId,
                running.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Succeeded));
            var compensating = await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                operationId,
                succeeded.Version,
                ProvisioningOperationStatuses.Compensating,
                ProvisioningStepIds.PrepareStorage));
            await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
                operationId,
                compensating.Version,
                ProvisioningOperationStatuses.Compensating,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Compensating));
        }

        await using var reloaded = CreateDbContext(root.Path);
        var decision = await new ProvisioningRecoveryService(CreateOperationStore(reloaded))
            .ClassifyAsync(operationId, CancellationToken.None);

        Assert.Equal(ProvisioningRecoveryDecisions.ManualIntervention, decision.Decision);
        Assert.Equal("compensation_incomplete", decision.ReasonCode);
        Assert.Equal(ProvisioningStepIds.PrepareStorage, decision.StepId);
    }

    [Fact]
    public async Task InterruptedReadOnlyStepIsSafeToResumeWithinAttemptPolicy()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var customSteps = ProvisioningPipeline.Steps.Select(step => new ProvisioningStepPlan(
            step.Id,
            step.Sequence,
            step.RetryClassification,
            step.SideEffectClassification,
            step.MaxAttempts,
            CompletedBeforeReservation: step.Sequence < 8)).ToArray();
        var plan = CreatePersistencePlan("server-one") with { Steps = customSteps };
        var reservation = await store.ReserveProvisioningPlanAsync(plan);
        var operations = CreateOperationStore(database);
        var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
        await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            reservation.ProvisioningOperationId,
            pending!.Version,
            ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.VerifyHealth,
            ProvisioningStepIds.VerifyHealth,
            ProvisioningStepStatuses.Running));

        var decision = await new ProvisioningRecoveryService(operations)
            .ClassifyAsync(reservation.ProvisioningOperationId, CancellationToken.None);

        Assert.Equal(ProvisioningRecoveryDecisions.Resume, decision.Decision);
        Assert.Equal("safe_step_retryable", decision.ReasonCode);
    }

    [Fact]
    public async Task ReconcilerOutcomesNeverBlindlyAdvancePersistedMutation()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var store = CreateStore(database);
        var reservation = await store.ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        var operations = CreateOperationStore(database);
        var pending = await operations.GetAsync(reservation.ProvisioningOperationId);
        await operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            reservation.ProvisioningOperationId,
            pending!.Version,
            ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepStatuses.Running));
        var recovery = new ProvisioningRecoveryService(operations);

        var absent = await recovery.ReconcileAsync(reservation.ProvisioningOperationId,
            new FakeReconciler(ProvisioningReconciliationOutcomes.EffectAbsent), CancellationToken.None);
        var exists = await recovery.ReconcileAsync(reservation.ProvisioningOperationId,
            new FakeReconciler(ProvisioningReconciliationOutcomes.EffectExists), CancellationToken.None);
        var ambiguous = await recovery.ReconcileAsync(reservation.ProvisioningOperationId,
            new FakeReconciler(ProvisioningReconciliationOutcomes.Ambiguous), CancellationToken.None);
        var unchanged = await operations.GetAsync(reservation.ProvisioningOperationId);

        Assert.Equal(ProvisioningRecoveryDecisions.Resume, absent.Decision);
        Assert.Equal(ProvisioningRecoveryDecisions.ManualIntervention, exists.Decision);
        Assert.Equal(ProvisioningRecoveryDecisions.ManualIntervention, ambiguous.Decision);
        Assert.Equal(ProvisioningStepStatuses.Running,
            unchanged!.Steps.Single(step => step.StepId == ProvisioningStepIds.PrepareStorage).Status);
    }

    [Fact]
    public async Task OptimisticVersionAllowsOnlyOneWorkerToAdvanceCheckpoint()
    {
        using var root = TemporaryDirectory.Create();
        await using (var database = CreateInitializedDbContext(root.Path))
        {
            await CreateStore(database).ReserveProvisioningPlanAsync(CreatePersistencePlan("server-one"));
        }

        await using var firstDatabase = CreateDbContext(root.Path);
        await using var secondDatabase = CreateDbContext(root.Path);
        var firstStore = CreateOperationStore(firstDatabase);
        var secondStore = CreateOperationStore(secondDatabase);
        var first = (await firstStore.GetIncompleteAsync()).Single();
        var second = (await secondStore.GetIncompleteAsync()).Single();
        var advanced = await firstStore.ApplyCheckpointAsync(new ProvisioningCheckpoint(
            first.OperationId,
            first.Version,
            ProvisioningOperationStatuses.Running,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepIds.PrepareStorage,
            ProvisioningStepStatuses.Running));

        await Assert.ThrowsAsync<ProvisioningConcurrencyException>(() => secondStore.ApplyCheckpointAsync(
            new ProvisioningCheckpoint(
                second.OperationId,
                second.Version,
                ProvisioningOperationStatuses.Running,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepIds.PrepareStorage,
                ProvisioningStepStatuses.Running)));

        var step = advanced.Steps.Single(item => item.StepId == ProvisioningStepIds.PrepareStorage);
        Assert.Equal(first.Version + 1, advanced.Version);
        Assert.Equal(1, step.Attempt);
        Assert.Equal(TimeSpan.Zero, step.StartedAtUtc!.Value.Offset);
    }

    [Fact]
    public async Task DifferentServersCanCompleteWithoutGlobalLock()
    {
        using var root = TemporaryDirectory.Create();
        await using var database = CreateInitializedDbContext(root.Path);
        var first = CreateHarness(database, _ => CreateValidatedPlan("one", 8501, "servers/one/data"));
        var second = CreateHarness(database, _ => CreateValidatedPlan("two", 8502, "servers/two/data"));

        var firstResult = await first.Service.StartProvisioningAsync(CreateRequest("one"), CancellationToken.None);
        var secondResult = await second.Service.StartProvisioningAsync(CreateRequest("two"), CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(2, await database.ManagedGameServers.CountAsync());
    }

    private static ProvisioningPlanBuilder CreatePlanBuilder(GameDefinition definition, HostCapabilitySnapshot host) =>
        new(
            new GameDefinitionRegistry([definition]),
            new StubHostCapabilityService(host),
            new GameRequirementEvaluator(),
            new StubPortPlanner(),
            new StubStoragePlanner());

    private static ProvisioningHarness CreateHarness(
        GamesHudDbContext database,
        Func<CreateGameServerProvisioningRequest, ValidatedProvisioningPlan>? planFactory = null)
    {
        var store = CreateStore(database);
        var operations = CreateOperationStore(database);
        var builder = new StubPlanBuilder(planFactory ?? (request => CreateValidatedPlan(
            request.GameServerId, 8211, $"servers/{request.GameServerId}/data")));
        var engine = new ProvisioningEngine(operations, CreateSteps(), NullLogger<ProvisioningEngine>.Instance);
        return new ProvisioningHarness(
            store,
            operations,
            new GameServerProvisioningService(builder, store, operations, engine));
    }

    private static IReadOnlyCollection<IProvisioningStep> CreateSteps(params IProvisioningStep[] overrides)
    {
        var byId = overrides.ToDictionary(step => step.Id, StringComparer.Ordinal);
        return ProvisioningStepIds.ExecutableFoundation
            .Select(id => byId.TryGetValue(id, out var step)
                ? step
                : new NoHostMutationProvisioningStep(id))
            .ToArray();
    }

    private static ProvisioningContext CreateContext(ManagedServerReservationResult reservation)
    {
        var plan = CreateValidatedPlan(reservation.GameServerId, 8211, $"servers/{reservation.GameServerId}/data");
        return new ProvisioningContext(reservation.ProvisioningOperationId, new PalworldGameDefinition(), plan, reservation);
    }

    private static ValidatedProvisioningPlan CreateValidatedPlan(string serverId, int port, string storagePath) =>
        new(
            new GameServerId(serverId),
            new GameId("palworld"),
            $"Server {serverId}",
            "docker",
            GameCompatibilityStatuses.Compatible,
            [],
            [new ValidatedProvisioningPort("game", "tcp", port, "public")],
            [new ValidatedProvisioningStorage("data", storagePath)],
            [],
            ProvisioningStepIds.All);

    private static ManagedServerProvisioningPlan CreatePersistencePlan(
        string serverId,
        int port = 8211,
        string? storagePath = null) =>
        new(
            serverId,
            "palworld",
            $"Server {serverId}",
            "docker",
            [new PortReservationPlan("game", "tcp", port, "public")],
            [new StorageReservationPlan("data", storagePath ?? $"servers/{serverId}/data")]);

    private static CreateGameServerProvisioningRequest CreateRequest(string serverId) =>
        new(serverId, "palworld", $"Server {serverId}");

    private static HostCapabilitySnapshot CreateCompatibleHost(
        int logicalProcessors = 4,
        string architecture = "x64") =>
        new(
            new HostOperatingSystemInfo("linux", "Linux", architecture),
            new HostCpuInfo(logicalProcessors, architecture),
            new HostMemoryInfo(HostCapabilityStatuses.Available, 32UL * 1024 * 1024 * 1024, 24UL * 1024 * 1024 * 1024),
            new HostStorageInfo(HostCapabilityStatuses.Available, string.Empty, 1000UL * 1024 * 1024 * 1024, 900UL * 1024 * 1024 * 1024),
            new HostNetworkInfo(HostCapabilityStatuses.Available, 1, true, true, true),
            [new HostRuntimeInfo("docker", "Docker", HostCapabilityStatuses.Available, true, true, "1", "linux", [])],
            new HostReadinessInfo(HostReadinessStatuses.Ready, "Ready"),
            []);

    private static GamesHudDbContext CreateInitializedDbContext(string dataRoot)
    {
        var database = CreateDbContext(dataRoot);
        new PersistenceInitializer(
            database,
            new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })),
            Options.Create(new PersistenceOptions { AutoMigrate = true }))
            .InitializeAsync().GetAwaiter().GetResult();
        return database;
    }

    private static GamesHudDbContext CreateDbContext(string dataRoot)
    {
        var layout = new PersistenceLayoutResolver(Options.Create(new StorageOptions { DataRoot = dataRoot })).ResolveLayout();
        return new GamesHudDbContext(new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(layout.DatabasePath))
            .Options);
    }

    private static ManagedServerStore CreateStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database));

    private static ProvisioningOperationStore CreateOperationStore(GamesHudDbContext database) =>
        new(database, new EfCorePersistenceTransactionBoundary(database), new ProvisioningStateMachine());

    private sealed record ProvisioningHarness(
        ManagedServerStore Store,
        ProvisioningOperationStore Operations,
        GameServerProvisioningService Service);

    private sealed class StubPlanBuilder(Func<CreateGameServerProvisioningRequest, ValidatedProvisioningPlan> factory)
        : IProvisioningPlanBuilder
    {
        public Task<ProvisioningPlanBuildResult> BuildAsync(CreateGameServerProvisioningRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProvisioningPlanBuildResult(factory(request), new PalworldGameDefinition(), null));
    }

    private sealed class StubHostCapabilityService(HostCapabilitySnapshot snapshot) : IHostCapabilityService
    {
        public Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class StubPortPlanner : IPortPlanner
    {
        public Task<GamePortPlan> CreatePlanAsync(GameDefinition definition, CancellationToken cancellationToken)
        {
            var items = definition.Ports.Select(port => new GamePortPlanItem(
                port.Id, port.Label, port.Purpose, port.Exposure, port.Required, port.AllowAlternative,
                new PortAvailability(port.DefaultPort, PortAvailabilityStatuses.Available, true, [], "Available"),
                new PortAllocationResult(port.DefaultPort, port.DefaultPort, false, PortAllocationStatuses.Allocated, null, "Allocated", [port.DefaultPort]))).ToArray();
            return Task.FromResult(new GamePortPlan(definition.GameId.ToString(), definition.DisplayName, PortPlanStatuses.Ready, items, "Ready"));
        }
    }

    private sealed class StubStoragePlanner : IGameStoragePlanner
    {
        public GameStoragePlan CreatePlan(GameDefinition definition, GameServerId gameServerId)
        {
            if (gameServerId.ToString().Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException("Invalid id.");
            }

            var entries = definition.Storages.Select(storage => new GameStoragePlanEntry(
                storage.Id, storage.Label, storage.Purpose, StorageOwnerships.Managed,
                $"C:/internal/{gameServerId}/{storage.Id}", $"servers/{gameServerId}/{storage.Id}",
                storage.RuntimeTarget, storage.Persistent, storage.Required, storage.BackupEligible,
                storage.UserData, storage.MinimumBytes, StoragePlanStatuses.Ready)).ToArray();
            return new GameStoragePlan(gameServerId, definition.GameId.ToString(), definition.DisplayName,
                StoragePlanStatuses.Ready, "C:/internal", $"C:/internal/{gameServerId}", $"servers/{gameServerId}",
                StorageOwnerships.Managed, null, null, entries, [], [], "Ready");
        }
    }

    private sealed class FakeStep : IProvisioningStep, ICompensatingProvisioningStep
    {
        private readonly ProvisioningStepResult _result;
        private readonly Action? _compensation;
        private readonly Exception? _exception;

        public FakeStep(string id, ProvisioningStepResult? result = null, Action? compensation = null, Exception? exception = null)
        {
            Id = id;
            _result = result ?? ProvisioningStepResult.Success();
            _compensation = compensation;
            _exception = exception;
        }

        public string Id { get; }

        public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken) =>
            _exception is null ? Task.FromResult(_result) : Task.FromException<ProvisioningStepResult>(_exception);

        public Task CompensateAsync(ProvisioningContext context, CancellationToken cancellationToken)
        {
            _compensation?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ObservingStep(
        string id,
        Func<Task<ProvisioningStepResult>> execute) : IProvisioningStep
    {
        public string Id { get; } = id;

        public Task<ProvisioningStepResult> ExecuteAsync(
            ProvisioningContext context,
            CancellationToken cancellationToken) => execute();
    }

    private sealed class FakeReconciler(string outcome) : IProvisioningStepReconciler
    {
        public string StepId => ProvisioningStepIds.PrepareStorage;

        public Task<ProvisioningReconciliationResult> InspectAsync(
            ProvisioningOperationSnapshot operation,
            ProvisioningStepSnapshot step,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProvisioningReconciliationResult(outcome, "Synthetic reconciliation result."));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-provisioning-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
