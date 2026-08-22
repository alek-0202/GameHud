using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.HostCapabilities.Services;

namespace GamesHud.Api.GameServers.Storage;

public sealed class GameStoragePlanner : IGameStoragePlanner
{
    private readonly IManagedStoragePathBuilder _pathBuilder;
    private readonly IHostStorageInfoProvider _storageInfoProvider;

    public GameStoragePlanner(
        IManagedStoragePathBuilder pathBuilder,
        IHostStorageInfoProvider storageInfoProvider)
    {
        _pathBuilder = pathBuilder;
        _storageInfoProvider = storageInfoProvider;
    }

    public GameStoragePlan CreatePlan(GameDefinition definition, GameServerId gameServerId)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var layout = _pathBuilder.CreateLayout(gameServerId);
        var entries = definition.Storages
            .Select(storage => CreateEntry(layout, storage))
            .ToArray();
        var mounts = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.RuntimeTarget))
            .Select(entry => new StorageMount(
                entry.DefinitionId,
                entry.HostPath,
                entry.RuntimeTarget!,
                ReadOnly: false))
            .ToArray();
        var requiredBytes = SumRequiredBytes(entries);
        var issues = new List<GameStoragePlanIssue>();
        var serverRootExists = Directory.Exists(layout.ServerRoot);
        var availableBytes = GetAvailableBytes(layout.DataRoot, issues);

        if (serverRootExists)
        {
            issues.Add(new GameStoragePlanIssue(
                StorageIssueCodes.ExistingPath,
                "blocking",
                "Planned managed server path already exists. GamesHud will not adopt or overwrite it automatically."));
        }

        if (availableBytes is not null
            && requiredBytes is not null
            && availableBytes.Value < requiredBytes.Value)
        {
            issues.Add(new GameStoragePlanIssue(
                StorageIssueCodes.InsufficientStorage,
                "blocking",
                "Configured storage root does not have enough free space for the declared storage requirement."));
        }

        return new GameStoragePlan(
            gameServerId,
            definition.GameId.ToString(),
            definition.DisplayName,
            CalculateStatus(issues),
            layout.DataRoot,
            layout.ServerRoot,
            layout.ServerRelativePath,
            StorageOwnerships.Managed,
            requiredBytes,
            availableBytes,
            entries.Select(entry => entry with
            {
                Status = serverRootExists ? StoragePlanStatuses.Collision : entry.Status
            }).ToArray(),
            mounts,
            issues,
            "Storage planning is advisory until durable reservation exists.");
    }

    private static GameStoragePlanEntry CreateEntry(
        ManagedStorageLayout layout,
        GameStorageDefinition storage)
    {
        var entryRelativePath = Path.Combine(layout.ServerRelativePath, storage.Id);
        var hostPath = ManagedStoragePathBuilder.EnsureContained(
            layout.DataRoot,
            Path.Combine(layout.ServerRoot, storage.Id),
            "Planned storage entry escaped the managed data root.");

        return new GameStoragePlanEntry(
            storage.Id,
            storage.Label,
            storage.Purpose,
            StorageOwnerships.Managed,
            hostPath,
            entryRelativePath,
            storage.RuntimeTarget,
            storage.Persistent,
            storage.Required,
            storage.BackupEligible,
            storage.UserData,
            storage.MinimumBytes,
            StoragePlanStatuses.Ready);
    }

    private ulong? GetAvailableBytes(string dataRoot, ICollection<GameStoragePlanIssue> issues)
    {
        try
        {
            return _storageInfoProvider.GetDriveInfo(dataRoot).AvailableBytes;
        }
        catch (Exception)
        {
            issues.Add(new GameStoragePlanIssue(
                StorageIssueCodes.StorageUnknown,
                "warning",
                "Available space could not be inspected for the configured storage root."));

            return null;
        }
    }

    private static ulong? SumRequiredBytes(IReadOnlyCollection<GameStoragePlanEntry> entries)
    {
        var requiredEntries = entries
            .Where(entry => entry.Required && entry.MinimumBytes is not null)
            .Select(entry => entry.MinimumBytes!.Value)
            .ToArray();

        return requiredEntries.Length == 0 ? null : requiredEntries.Aggregate(0UL, (sum, value) => checked(sum + value));
    }

    private static string CalculateStatus(IReadOnlyCollection<GameStoragePlanIssue> issues)
    {
        if (issues.Any(issue => issue.Code == StorageIssueCodes.ExistingPath))
        {
            return StoragePlanStatuses.Collision;
        }

        if (issues.Any(issue => issue.Code == StorageIssueCodes.InsufficientStorage))
        {
            return StoragePlanStatuses.Insufficient;
        }

        if (issues.Any(issue => issue.Code == StorageIssueCodes.StorageUnknown))
        {
            return StoragePlanStatuses.Unknown;
        }

        return StoragePlanStatuses.Ready;
    }
}
