using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Requirements;

public static class GameCompatibilityStatuses
{
    public const string Compatible = "compatible";
    public const string CompatibleWithWarnings = "compatible_with_warnings";
    public const string Incompatible = "incompatible";
    public const string Unknown = "unknown";
}

public static class RequirementCheckStatuses
{
    public const string Passed = "passed";
    public const string Warning = "warning";
    public const string Failed = "failed";
    public const string Unknown = "unknown";
}

public static class RequirementIssueSeverities
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
}

public sealed record GameCompatibilityAssessment(
    GameId GameId,
    string DisplayName,
    string Status,
    IReadOnlyCollection<GameCompatibilityCheck> Checks,
    IReadOnlyCollection<GameCompatibilityIssue> BlockingIssues,
    IReadOnlyCollection<GameCompatibilityIssue> Warnings);

public sealed record GameCompatibilityCheck(
    string Id,
    string Label,
    string Required,
    string Detected,
    string Status,
    string Message);

public sealed record GameCompatibilityIssue(
    string Code,
    string Severity,
    string Message);
