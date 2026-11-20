using GamesHud.Api.GameServers.Definitions;

namespace GamesHud.Api.GameServers.Ports;

public interface IPortPlanner
{
    Task<GamePortPlan> CreatePlanAsync(
        GameDefinition definition,
        CancellationToken cancellationToken);

    Task<GamePortPlan> CreatePlanAsync(
        GameDefinition definition,
        IReadOnlyDictionary<string, int> preferredHostPorts,
        CancellationToken cancellationToken) =>
        preferredHostPorts.Count == 0
            ? CreatePlanAsync(definition, cancellationToken)
            : throw new NotSupportedException("Preferred host ports are not supported by this planner.");
}
