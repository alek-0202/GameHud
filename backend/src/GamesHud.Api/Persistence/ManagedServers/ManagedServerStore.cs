using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence.ManagedServers;

public sealed class ManagedServerStore : IManagedServerStore
{
    private const string InitialProvisioningStep = "reserved";

    private readonly GamesHudDbContext _dbContext;
    private readonly IPersistenceTransactionBoundary _transactionBoundary;

    public ManagedServerStore(
        GamesHudDbContext dbContext,
        IPersistenceTransactionBoundary transactionBoundary)
    {
        _dbContext = dbContext;
        _transactionBoundary = transactionBoundary;
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
                };
                var portReservations = normalizedPlan.Ports
                    .Select(port => new PortReservationRecord
                    {
                        Id = CreateId(),
                        GameServerId = normalizedPlan.GameServerId,
                        PortDefinitionId = port.PortDefinitionId,
                        Protocol = port.Protocol,
                        Port = port.Port,
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
                        Ownership = StorageOwnerships.Managed,
                        Status = ReservationStatuses.Reserved,
                        ProvisioningOperationId = operationId,
                    })
                    .ToArray();

                dbContext.ManagedGameServers.Add(gameServer);
                dbContext.ProvisioningOperations.Add(operation);
                dbContext.PortReservations.AddRange(portReservations);
                dbContext.StorageReservations.AddRange(storageReservations);

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

    private static ManagedServerProvisioningPlan NormalizePlan(ManagedServerProvisioningPlan plan)
    {
        var gameServerId = NormalizeGameServerId(plan.GameServerId);
        var gameId = NormalizeRequiredIdentifier(plan.GameId, nameof(plan.GameId));
        var displayName = NormalizeDisplayName(plan.DisplayName);
        var runtimeType = NormalizeRequiredIdentifier(plan.RuntimeType, nameof(plan.RuntimeType));
        var ports = plan.Ports.Select(NormalizePort).ToArray();
        var storage = plan.Storage
            .Select(item => NormalizeStorage(item, gameServerId))
            .ToArray();

        return new ManagedServerProvisioningPlan(
            gameServerId,
            gameId,
            displayName,
            runtimeType,
            ports,
            storage);
    }

    private static PortReservationPlan NormalizePort(PortReservationPlan port)
    {
        var networkPort = new NetworkPort(port.Port, port.Protocol);
        var exposure = NormalizeRequiredIdentifier(port.Exposure, nameof(port.Exposure));

        if (exposure is not PortExposures.Public and not PortExposures.Internal)
        {
            throw new ArgumentException("Unsupported port exposure.", nameof(port));
        }

        return new PortReservationPlan(
            NormalizeRequiredIdentifier(port.PortDefinitionId, nameof(port.PortDefinitionId)),
            networkPort.Protocol,
            networkPort.Number,
            exposure);
    }

    private static StorageReservationPlan NormalizeStorage(
        StorageReservationPlan storage,
        string gameServerId)
    {
        var storageDefinitionId = NormalizeRequiredIdentifier(
            storage.StorageDefinitionId,
            nameof(storage.StorageDefinitionId));
        var relativePath = string.IsNullOrWhiteSpace(storage.RelativePath)
            ? CreateManagedRelativePath(gameServerId, storageDefinitionId)
            : NormalizeManagedRelativePath(storage.RelativePath);

        return new StorageReservationPlan(storageDefinitionId, relativePath);
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
