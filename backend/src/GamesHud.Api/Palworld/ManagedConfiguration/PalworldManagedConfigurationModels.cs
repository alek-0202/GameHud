using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.Palworld.ManagedConfiguration;

public static class PalworldManagedConfigurationErrorCodes
{
    public const string TargetInvalid = "palworld_configuration_target_unsafe";
    public const string IntentInvalid = "palworld_configuration_intent_invalid";
    public const string SecretUnavailable = "palworld_configuration_secret_unavailable";
    public const string ExistingInvalid = "palworld_configuration_existing_invalid";
    public const string DriftDetected = "palworld_configuration_drift";
    public const string WriteFailed = "palworld_configuration_write_failed";
    public const string OutcomeUnknown = "palworld_configuration_outcome_unknown";
}

public static class PalworldManagedConfigurationMutationStatuses
{
    public const string Success = "success";
    public const string KnownFailure = "known_failure";
    public const string Unknown = "unknown";
}

public sealed record PalworldManagedConfiguration(
    string ServerName,
    string ServerDescription,
    int MaxPlayers,
    string Difficulty,
    string ServerPassword,
    string AdminPassword);

public sealed record PalworldManagedConfigurationTarget(
    GameServerId GameServerId,
    string OperationId,
    string DataRoot,
    string StorageRoot,
    string DestinationDirectory,
    string DestinationPath,
    string TemporaryFilePrefix);

public sealed record PalworldManagedConfigurationTargetBuildResult(
    PalworldManagedConfigurationTarget? Target,
    string? SafeErrorCode,
    string? SafeMessage)
{
    public bool Succeeded => Target is not null;
}

public sealed record PalworldManagedConfigurationMutationResult(
    string Status,
    string? SafeErrorCode,
    string SafeMessage)
{
    public static PalworldManagedConfigurationMutationResult Success() =>
        new(PalworldManagedConfigurationMutationStatuses.Success, null, "Managed Palworld configuration is materialized.");

    public static PalworldManagedConfigurationMutationResult KnownFailure(string code, string message) =>
        new(PalworldManagedConfigurationMutationStatuses.KnownFailure, code, message);

    public static PalworldManagedConfigurationMutationResult Unknown() =>
        new(PalworldManagedConfigurationMutationStatuses.Unknown,
            PalworldManagedConfigurationErrorCodes.OutcomeUnknown,
            "Managed Palworld configuration outcome could not be proven safely.");
}

public sealed class PalworldManagedConfigurationException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}

internal static class PalworldManagedPathSafety
{
    public static string EnsureContained(string root, string candidate)
    {
        _ = ManagedStoragePathBuilder.EnsureContained(
            root, candidate, "Managed Palworld configuration target escaped managed storage.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = $"{fullRoot}{Path.DirectorySeparatorChar}";
        if (!fullCandidate.Equals(fullRoot, comparison) && !fullCandidate.StartsWith(prefix, comparison))
            throw new PalworldManagedConfigurationException(
                PalworldManagedConfigurationErrorCodes.TargetInvalid,
                "Managed Palworld configuration target could not be proven safe.");
        return fullCandidate;
    }

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
