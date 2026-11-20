using GamesHud.Api.GameServers.Contracts;

namespace GamesHud.Api.GameServers.Ports;

public static class GamePortContractMapper
{
    public static PortAvailabilityResponse Map(PortAvailability availability)
    {
        return new PortAvailabilityResponse(
            availability.Port.Number,
            availability.Port.Protocol,
            availability.Status,
            availability.IsAvailable,
            availability.DockerPublishedPorts.Count > 0,
            availability.Message);
    }

    public static GamePortPlanResponse Map(GamePortPlan plan)
    {
        return new GamePortPlanResponse(
            plan.GameId,
            plan.DisplayName,
            plan.Status,
            plan.Ports.Select(Map).ToArray(),
            plan.Message);
    }

    private static GamePortPlanItemResponse Map(GamePortPlanItem item)
    {
        return new GamePortPlanItemResponse(
            item.DefinitionId,
            item.Label,
            item.Purpose,
            item.Exposure,
            item.Required,
            item.AllowAlternative,
            Map(item.Availability),
            Map(item.Allocation));
    }

    private static PortAllocationResponse Map(PortAllocationResult allocation)
    {
        return new PortAllocationResponse(
            Map(allocation.RequestedPort),
            allocation.AllocatedPort is null ? null : Map(allocation.AllocatedPort),
            allocation.UsedAlternative,
            allocation.Status,
            allocation.ErrorCode,
            allocation.Message,
            allocation.CheckedPorts.Select(Map).ToArray());
    }

    private static NetworkPortResponse Map(NetworkPort port)
    {
        return new NetworkPortResponse(port.Number, port.Protocol);
    }
}
