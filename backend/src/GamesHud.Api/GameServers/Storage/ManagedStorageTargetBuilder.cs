using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;

namespace GamesHud.Api.GameServers.Storage;

public interface IManagedStorageTargetBuilder
{
    Task<ManagedStorageTargetBuildResult> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken);
    Task<ManagedStorageTargetBuildResult> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken);
}

public sealed class ManagedStorageTargetBuilder : IManagedStorageTargetBuilder
{
    private readonly IManagedServerStore _store;
    private readonly IManagedStoragePathBuilder _paths;
    private readonly IGameDefinitionRegistry _definitions;

    public ManagedStorageTargetBuilder(IManagedServerStore store, IManagedStoragePathBuilder paths,
        IGameDefinitionRegistry definitions)
    {
        _store = store;
        _paths = paths;
        _definitions = definitions;
    }

    public async Task<ManagedStorageTargetBuildResult> BuildAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(context.GameServerId.ToString(), cancellationToken);
        return Build(context.OperationId, context.GameServerId, context.GameDefinition, server,
            context.ReservedResources.StorageReservationIds, context.ValidatedPlan.Storage);
    }

    public async Task<ManagedStorageTargetBuildResult> BuildForReconciliationAsync(
        string operationId, GameServerId gameServerId, CancellationToken cancellationToken)
    {
        var server = await _store.GetManagedServerAsync(gameServerId.ToString(), cancellationToken);
        if (server is null || !_definitions.TryGet(new GameId(server.GameId), out var definition))
            return Failed(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid.");
        var reservationIds = server?.StorageReservations
            .Where(item => item.ProvisioningOperationId == operationId)
            .Select(item => item.Id).ToArray() ?? [];
        return Build(operationId, gameServerId, definition!, server, reservationIds, plannedStorage: null);
    }

    private ManagedStorageTargetBuildResult Build(string operationId, GameServerId gameServerId,
        GameDefinition definition, ManagedGameServerRecord? server, IReadOnlyCollection<string> expectedReservationIds,
        IReadOnlyCollection<ValidatedProvisioningStorage>? plannedStorage)
    {
        if (server is null || server.InstallationType != ManagedInstallationTypes.Managed)
            return Failed(ManagedStorageErrorCodes.OwnershipInvalid, "Managed storage ownership could not be proven.");

        var reservationIds = expectedReservationIds.ToHashSet(StringComparer.Ordinal);
        var reservations = server.StorageReservations.Where(item => reservationIds.Contains(item.Id)).ToArray();
        if (reservations.Length != reservationIds.Count || reservations.Any(item =>
            item.GameServerId != server.Id || item.ProvisioningOperationId != operationId
            || item.Status != ReservationStatuses.Reserved || item.Ownership != StorageOwnerships.Managed))
            return Failed(ManagedStorageErrorCodes.OwnershipInvalid, "Managed storage ownership could not be proven.");

        var entries = new List<ManagedStorageTargetEntry>();
        string? apiRoot = null;
        foreach (var reservation in reservations)
        {
            var storageDefinition = definition.Storages.SingleOrDefault(item => item.Id == reservation.StorageDefinitionId);
            var planned = plannedStorage?.SingleOrDefault(item => item.DefinitionId == reservation.StorageDefinitionId);
            var expectedRelative = $"servers/{gameServerId}/{reservation.StorageDefinitionId}";
            var actualRelative = reservation.RelativePath.Replace('\\', '/');
            if (storageDefinition is null || plannedStorage is not null && (planned is null || planned.RelativePath != actualRelative
                    || planned.ApiPath is not null && !PathEquals(planned.ApiPath, reservation.ApiPath)
                    || planned.HostPath is not null && !PathEquals(planned.HostPath, reservation.HostPath))
                || actualRelative != expectedRelative)
                return Failed(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid.");

            try
            {
                var absolute = ResolvePersistedPath(reservation.ApiPath, actualRelative,
                    _paths.CreateLayout(gameServerId).DataRoot);
                var root = GetPersistedRoot(absolute, actualRelative);
                apiRoot ??= root;
                if (!PathEquals(apiRoot, root)
                    || absolute.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return Failed(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid.");
                entries.Add(new(reservation.Id, reservation.StorageDefinitionId, actualRelative, absolute));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or StoragePlanningException)
            {
                return Failed(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid.");
            }
        }

        return entries.Count == 0
            ? Failed(ManagedStorageErrorCodes.TargetInvalid, "Managed storage target is invalid.")
            : new(new(gameServerId, operationId, apiRoot!, entries), null, null);
    }

    internal static string ResolvePersistedPath(string persistedPath, string relativePath, string historicalRoot)
    {
        var candidate = string.IsNullOrWhiteSpace(persistedPath)
            ? Path.Combine(historicalRoot, relativePath)
            : persistedPath;
        var full = Path.GetFullPath(candidate);
        _ = GetPersistedRoot(full, relativePath);
        return full;
    }

    internal static string GetPersistedRoot(string absolutePath, string relativePath)
    {
        var full = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var suffix = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var marker = $"{Path.DirectorySeparatorChar}{suffix}";
        if (!full.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            throw new StoragePlanningException(StorageIssueCodes.InvalidGameServerId,
                "Persisted storage path does not match its managed relative path.");
        var root = full[..^marker.Length];
        _ = ManagedStoragePathBuilder.EnsureContained(root, full,
            "Persisted storage path escaped its durable root.");
        return Path.GetFullPath(root);
    }

    private static bool PathEquals(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static ManagedStorageTargetBuildResult Failed(string code, string message) => new(null, code, message);
}
