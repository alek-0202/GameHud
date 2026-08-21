using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.HostCapabilities.Models;

namespace GamesHud.Api.GameServers.Requirements;

public interface IGameRequirementEvaluator
{
    GameCompatibilityAssessment Evaluate(
        GameDefinition definition,
        HostCapabilitySnapshot hostCapabilities);
}
