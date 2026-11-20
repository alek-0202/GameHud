using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Storage;

public interface IGameStoragePlanner
{
    GameStoragePlan CreatePlan(GameDefinition definition, GameServerId gameServerId);
}
