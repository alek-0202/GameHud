using GamesHud.Api.GameServers.Contracts;

namespace GamesHud.Api.GameServers.Requirements;

public static class GameCompatibilityContractMapper
{
    public static GameCompatibilityAssessmentResponse Map(GameCompatibilityAssessment assessment)
    {
        return new GameCompatibilityAssessmentResponse(
            assessment.GameId.ToString(),
            assessment.DisplayName,
            assessment.Status,
            assessment.Checks.Select(Map).ToArray(),
            assessment.BlockingIssues.Select(Map).ToArray(),
            assessment.Warnings.Select(Map).ToArray());
    }

    private static GameCompatibilityCheckResponse Map(GameCompatibilityCheck check)
    {
        return new GameCompatibilityCheckResponse(
            check.Id,
            check.Label,
            check.Required,
            check.Detected,
            check.Status,
            check.Message);
    }

    private static GameCompatibilityIssueResponse Map(GameCompatibilityIssue issue)
    {
        return new GameCompatibilityIssueResponse(
            issue.Code,
            issue.Severity,
            issue.Message);
    }
}
