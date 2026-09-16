using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence.ManagedServers;

public sealed class ManagedServerStore : IManagedServerStore
{
    private const string InitialProvisioningStep = "reserve_resources";

    private readonly GamesHudDbContext _dbContext;
    private readonly IPersistenceTransactionBoundary _transactionBoundary;
    private readonly IManagedStoragePathBuilder? _paths;

    public ManagedServerStore(
        GamesHudDbContext dbContext,
        IPersistenceTransactionBoundary transactionBoundary,
        IManagedStoragePathBuilder? paths = null)
    {
        _dbContext = dbContext;
        _transactionBoundary = transactionBoundary;
        _paths = paths;
    }

    public Task<ManagedServerReservationResult> ReserveProvisioningPlanAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var normalizedPlan = NormalizePlan(plan);

        return _transactionBoundary.ExecuteAsync(
            async (dbContext, token) =>
            {
                var operationId = CreateId();
                var gameServer = new ManagedGameServerRecord
                {
                    Id = normalizedPlan.GameServerId,
                    GameId = normalizedPlan.GameId,
                    DisplayName = normalizedPlan.DisplayName,
                    InstallationType = ManagedInstallationTypes.Managed,
                    RuntimeType = normalizedPlan.RuntimeType,
                    LifecycleState = ManagedGameServerLifecycleStates.PendingProvisioning,
                };
                var operation = new ProvisioningOperationRecord
                {
                    Id = operationId,
                    GameServerId = normalizedPlan.GameServerId,
                    Type = ProvisioningOperationTypes.Provision,
                    Status = ProvisioningOperationStatuses.Pending,
                    ActiveSlot = ProvisioningOperationActiveSlots.Active,
                    CurrentStep = InitialProvisioningStep,
                    PipelineVersion = normalizedPlan.PipelineVersion!,
                    Version = 1,
                };
                var now = DateTimeOffset.UtcNow;
                var steps = normalizedPlan.Steps!
                    .Select(step => new ProvisioningStepRecord
                    {
                        Id = CreateId(),
                        OperationId = operationId,
                        StepId = step.StepId,
                        Sequence = step.Sequence,
                        Status = step.CompletedBeforeReservation
                            ? ProvisioningStepStatuses.Succeeded
                            : ProvisioningStepStatuses.Pending,
                        Attempt = step.CompletedBeforeReservation ? 1 : 0,
                        RetryClassification = step.RetryClassification,
                        SideEffectClassification = step.SideEffectClassification,
                        MaxAttempts = step.MaxAttempts,
                        StartedAtUtc = step.CompletedBeforeReservation ? now : null,
                        CompletedAtUtc = step.CompletedBeforeReservation ? now : null,
                    })
                    .ToArray();
                var portReservations = normalizedPlan.Ports
                    .Select(port => new PortReservationRecord
                    {
                        Id = CreateId(),
                        GameServerId = normalizedPlan.GameServerId,
                        PortDefinitionId = port.PortDefinitionId,
                        Protocol = port.Protocol,
                        Port = port.HostPort ?? port.ContainerPort,
                        ContainerPort = port.ContainerPort,
                        HostPort = port.HostPort,
                        Published = port.Published,
                        Exposure = port.Exposure,
                        Status = ReservationStatuses.Reserved,
                        ProvisioningOperationId = operationId,
                    })
                    .ToArray();
                var storageReservations = normalizedPlan.Storage
                    .Select(storage => new StorageReservationRecord
                    {
                        Id = CreateId(),
                        GameServerId = normalizedPlan.GameServerId,
                        StorageDefinitionId = storage.StorageDefinitionId,
                        RelativePath = storage.RelativePath ?? CreateManagedRelativePath(
                            normalizedPlan.GameServerId,
                            storage.StorageDefinitionId),
                        ApiPath = storage.ApiPath!,
                        HostPath = storage.HostPath!,
                        Ownership = StorageOwnerships.Managed,
                        Status = ReservationStatuses.Reserved,
                        ProvisioningOperationId = operationId,
                    })
                    .ToArray();
                var configuration = normalizedPlan.Configuration is null ? null : new ManagedGameConfigurationRecord
                {
                    Id = CreateId(),
                    GameServerId = normalizedPlan.GameServerId,
                    GameId = normalizedPlan.Configuration.GameId.Value,
                    ConfigurationKind = normalizedPlan.Configuration.ConfigurationKind,
                    SchemaVersion = normalizedPlan.Configuration.SchemaVersion,
                    Payload = normalizedPlan.Configuration.Payload,
                    Version = 1
                };

                dbContext.ManagedGameServers.Add(gameServer);
                dbContext.ProvisioningOperations.Add(operation);
                dbContext.ProvisioningSteps.AddRange(steps);
                dbContext.PortReservations.AddRange(portReservations);
                dbContext.StorageReservations.AddRange(storageReservations);
                if (configuration is not null) dbContext.ManagedGameConfigurations.Add(configuration);
                if (normalizedPlan.RuntimeImage is not null)
                    dbContext.RuntimeImageIntents.Add(RuntimeImageIntentStore.Create(operationId, normalizedPlan));

                await Task.CompletedTask;

                return new ManagedServerReservationResult(
                    normalizedPlan.GameServerId,
                    operationId,
                    portReservations.Select(port => port.Id).ToArray(),
                    storageReservations.Select(storage => storage.Id).ToArray());
            },
            cancellationToken);
    }

    public Task<ManagedGameServerRecord?> GetManagedServerAsync(
        string gameServerId,
        CancellationToken cancellationToken = default)
    {
        var normalizedGameServerId = NormalizeGameServerId(gameServerId);

        return _dbContext.ManagedGameServers
            .Include(server => server.PortReservations)
            .Include(server => server.StorageReservations)
            .Include(server => server.ProvisioningOperations)
            .Include(server => server.Configurations)
            .SingleOrDefaultAsync(server => server.Id == normalizedGameServerId, cancellationToken);
    }

    public Task<ProvisioningOperationRecord?> GetActiveOperationAsync(
        string gameServerId,
        CancellationToken cancellationToken = default)
    {
        var normalizedGameServerId = NormalizeGameServerId(gameServerId);

        return _dbContext.ProvisioningOperations
            .SingleOrDefaultAsync(
                operation =>
                    operation.GameServerId == normalizedGameServerId
                    && operation.ActiveSlot == ProvisioningOperationActiveSlots.Active,
                cancellationToken);
    }

    public async Task<ManagedServerReservationConflict?> FindReservationConflictAsync(
        ManagedServerProvisioningPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var normalized = NormalizePlan(plan);

        foreach (var port in normalized.Ports)
        {
            if (port.Published && await _dbContext.PortReservations.AnyAsync(
                item => item.Published && item.Protocol == port.Protocol && item.HostPort == port.HostPort,
                cancellationToken))
            {
                return new ManagedServerReservationConflict(
                    "port_conflict",
                    "A planned port is already reserved.");
            }
        }

        foreach (var storage in normalized.Storage)
        {
            if (await _dbContext.StorageReservations.AnyAsync(
                item => item.RelativePath == storage.RelativePath,
                cancellationToken))
            {
                return new ManagedServerReservationConflict(
                    "storage_conflict",
                    "A planned storage path is already reserved.");
            }
        }

        return null;
    }


    private ManagedServerProvisioningPlan NormalizePlan(ManagedServerProvisioningPlan plan)
    {
        var gameServerId = NormalizeGameServerId(plan.GameServerId);
        var gameId = NormalizeRequiredIdentifier(plan.GameId, nameof(plan.GameId));
        var displayName = NormalizeDisplayName(plan.DisplayName);
        var runtimeType = NormalizeRequiredIdentifier(plan.RuntimeType, nameof(plan.RuntimeType));
        var ports = plan.Ports.Select(NormalizePort).ToArray();
        var storage = plan.Storage
            .Select(item => NormalizeStorage(item, gameServerId))
            .ToArray();
        var pipelineVersion = string.IsNullOrWhiteSpace(plan.PipelineVersion)
            ? ProvisioningPipelines.DefaultVersion
            : plan.PipelineVersion.Trim();
        var pipeline = ProvisioningPipelines.Find(pipelineVersion)
            ?? throw new ArgumentException("Unsupported provisioning pipeline.", nameof(plan));
        if (pipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion
            ? plan.RuntimeImage is null || !plan.RuntimeImage.IsPinned || plan.RuntimeImage.RuntimeType != runtimeType
            : plan.RuntimeImage is not null)
            throw new ArgumentException("Runtime image intent does not match the pipeline.", nameof(plan));
        var sourceSteps = plan.Steps ?? pipeline.Steps.Select(step => new ProvisioningStepPlan(
            step.Id,
            step.Sequence,
            step.RetryClassification,
            step.SideEffectClassification,
            step.MaxAttempts,
            step.Sequence <= 3)).ToArray();
        var steps = NormalizeSteps(sourceSteps);
        if (pipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion
            && (steps.Count != pipeline.Steps.Count || pipeline.Steps.Any(expected => !steps.Any(actual =>
                actual.StepId == expected.Id && actual.Sequence == expected.Sequence
                && actual.RetryClassification == expected.RetryClassification && actual.MaxAttempts == expected.MaxAttempts
                && actual.SideEffectClassification == expected.SideEffectClassification
                && actual.CompletedBeforeReservation == (expected.Sequence <= 3)))))
            throw new ArgumentException("V2 pipeline metadata is invalid.", nameof(plan));

        if (plan.Configuration is not null
            && (plan.Configuration.GameId.Value != gameId || plan.Configuration.Payload.Length > 8000))
        {
            throw new ArgumentException("Game configuration does not match the managed game.", nameof(plan));
        }

        if (pipelineVersion.Length > 40)
        {
            throw new ArgumentException("Pipeline version is too long.", nameof(plan));
        }

        return new ManagedServerProvisioningPlan(
            gameServerId,
            gameId,
            displayName,
            runtimeType,
            ports,
            storage,
            plan.Configuration,
            pipelineVersion,
            steps,
            plan.RuntimeImage);
    }

    private static IReadOnlyCollection<ProvisioningStepPlan> NormalizeSteps(
        IEnumerable<ProvisioningStepPlan> steps)
    {
        var normalized = steps.Select(step => new ProvisioningStepPlan(
            NormalizeRequiredIdentifier(step.StepId, nameof(step.StepId)),
            step.Sequence > 0 ? step.Sequence : throw new ArgumentOutOfRangeException(nameof(step.Sequence)),
            NormalizeRequiredIdentifier(step.RetryClassification, nameof(step.RetryClassification)),
            NormalizeRequiredIdentifier(step.SideEffectClassification, nameof(step.SideEffectClassification)),
            step.MaxAttempts > 0 ? step.MaxAttempts : throw new ArgumentOutOfRangeException(nameof(step.MaxAttempts)),
            step.CompletedBeforeReservation)).ToArray();

        if (normalized.Length == 0
            || normalized.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != normalized.Length
            || normalized.Select(step => step.Sequence).Distinct().Count() != normalized.Length)
        {
            throw new ArgumentException("Provisioning pipeline steps must have unique ids and sequences.", nameof(steps));
        }

        return normalized.OrderBy(step => step.Sequence).ToArray();
    }

    private static PortReservationPlan NormalizePort(PortReservationPlan port)
    {
        var containerPort = new NetworkPort(port.ContainerPort, port.Protocol);
        var exposure = NormalizeRequiredIdentifier(port.Exposure, nameof(port.Exposure));

        if (exposure is not PortExposures.Public and not PortExposures.Internal)
        {
            throw new ArgumentException("Unsupported port exposure.", nameof(port));
        }

        var shouldPublish = exposure == PortExposures.Public;
        if (port.Published != shouldPublish || shouldPublish != port.HostPort.HasValue)
            throw new ArgumentException("Port publication does not match its exposure.", nameof(port));
        var hostPort = port.HostPort.HasValue
            ? new NetworkPort(port.HostPort.Value, port.Protocol).Number
            : (int?)null;

        return new PortReservationPlan(
            NormalizeRequiredIdentifier(port.PortDefinitionId, nameof(port.PortDefinitionId)),
            containerPort.Protocol,
            containerPort.Number,
            hostPort,
            port.Published,
            exposure);
    }

    private StorageReservationPlan NormalizeStorage(
        StorageReservationPlan storage,
        string gameServerId)
    {
        var storageDefinitionId = NormalizeRequiredIdentifier(
            storage.StorageDefinitionId,
            nameof(storage.StorageDefinitionId));
        var relativePath = string.IsNullOrWhiteSpace(storage.RelativePath)
            ? CreateManagedRelativePath(gameServerId, storageDefinitionId)
            : NormalizeManagedRelativePath(storage.RelativePath);

        var apiPath = storage.ApiPath;
        var hostPath = storage.HostPath;
        if (string.IsNullOrWhiteSpace(apiPath) || string.IsNullOrWhiteSpace(hostPath))
        {
            var layout = _paths?.CreateLayout(new GameServerId(gameServerId));
            apiPath = layout is null ? string.Empty : ManagedStoragePathBuilder.EnsureContained(
                layout.DataRoot, Path.Combine(layout.DataRoot, relativePath),
                "Managed API path escaped its backend-controlled root.");
            hostPath = layout is null ? string.Empty : ManagedStoragePathBuilder.EnsureContained(
                layout.HostRoot, Path.Combine(layout.HostRoot, relativePath),
                "Managed Docker host path escaped its backend-controlled root.");
        }
        else
        {
            apiPath = Path.GetFullPath(apiPath);
            hostPath = Path.GetFullPath(hostPath);
            if (_paths is not null)
            {
                var layout = _paths.CreateLayout(new GameServerId(gameServerId));
                var expectedApi = ManagedStoragePathBuilder.EnsureContained(layout.DataRoot,
                    Path.Combine(layout.DataRoot, relativePath), "Managed API path escaped its backend-controlled root.");
                var expectedHost = ManagedStoragePathBuilder.EnsureContained(layout.HostRoot,
                    Path.Combine(layout.HostRoot, relativePath), "Managed Docker host path escaped its backend-controlled root.");
                if (!apiPath.Equals(expectedApi, StringComparison.OrdinalIgnoreCase)
                    || !hostPath.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Managed storage paths do not match backend-controlled roots.", nameof(storage));
            }
        }

        return new StorageReservationPlan(storageDefinitionId, relativePath, apiPath, hostPath);
    }

    private static string NormalizeGameServerId(string value)
    {
        return ManagedStoragePathBuilder.CreateSafeServerSegment(new GameServerId(value));
    }

    private static string NormalizeRequiredIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > 120
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();

        return normalized.Length > 200
            ? throw new ArgumentException("Display name is too long.", nameof(value))
            : normalized;
    }

    private static string CreateManagedRelativePath(string gameServerId, string storageDefinitionId)
    {
        return $"servers/{gameServerId}/{storageDefinitionId}";
    }

    private static string NormalizeManagedRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace('\\', '/');

        if (normalized.Length > 500
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')))
        {
            throw new ArgumentException("Managed storage relative path is invalid.", nameof(value));
        }

        if (!normalized.StartsWith("servers/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Managed storage relative path must be under servers/.", nameof(value));
        }

        return normalized.ToLowerInvariant();
    }

    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N");
    }

}
