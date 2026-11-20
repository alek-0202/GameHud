using GamesHud.Api.GameServers.Contracts;

namespace GamesHud.Api.GameServers.Storage;

public static class GameStorageContractMapper
{
    public static GameStoragePlanResponse Map(GameStoragePlan plan)
    {
        return new GameStoragePlanResponse(
            plan.GameServerId.ToString(),
            plan.GameId,
            plan.DisplayName,
            plan.Status,
            plan.Ownership,
            plan.RequiredBytes,
            plan.AvailableBytes,
            plan.Entries.Select(Map).ToArray(),
            plan.Mounts.Select(mount => Map(mount, plan.Entries)).ToArray(),
            plan.Issues.Select(Map).ToArray(),
            plan.Message);
    }

    private static GameStoragePlanEntryResponse Map(GameStoragePlanEntry entry)
    {
        return new GameStoragePlanEntryResponse(
            entry.DefinitionId,
            entry.Label,
            entry.Purpose,
            entry.Ownership,
            entry.RelativePath,
            entry.RuntimeTarget,
            entry.Persistent,
            entry.Required,
            entry.BackupEligible,
            entry.UserData,
            entry.MinimumBytes,
            entry.Status);
    }

    private static StorageMountResponse Map(
        StorageMount mount,
        IReadOnlyCollection<GameStoragePlanEntry> entries)
    {
        var entry = entries.Single(candidate => candidate.DefinitionId == mount.StorageDefinitionId);

        return new StorageMountResponse(
            mount.StorageDefinitionId,
            entry.RelativePath,
            mount.RuntimeTarget,
            mount.ReadOnly);
    }

    private static GameStoragePlanIssueResponse Map(GameStoragePlanIssue issue)
    {
        return new GameStoragePlanIssueResponse(issue.Code, issue.Severity, issue.Message);
    }
}
