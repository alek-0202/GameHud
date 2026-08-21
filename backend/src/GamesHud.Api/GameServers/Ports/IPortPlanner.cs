using GamesHud.Api.GameServers.Definitions;

namespace GamesHud.Api.GameServers.Ports;

public interface IPortPlanner
{
    Task<GamePortPlan> CreatePlanAsync(
        GameDefinition definition,
        CancellationToken cancellationToken);
}
