namespace GamesHud.Api.GameServers.Contracts;

public sealed record GameCompatibilityAssessmentResponse(
    string GameId,
    string DisplayName,
    string Status,
    IReadOnlyCollection<GameCompatibilityCheckResponse> Checks,
    IReadOnlyCollection<GameCompatibilityIssueResponse> BlockingIssues,
    IReadOnlyCollection<GameCompatibilityIssueResponse> Warnings);

public sealed record GameCompatibilityCheckResponse(
    string Id,
    string Label,
    string Required,
    string Detected,
    string Status,
    string Message);

public sealed record GameCompatibilityIssueResponse(
    string Code,
    string Severity,
    string Message);
