using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Ports;

public sealed class PortPlanner : IPortPlanner
{
    private readonly IPortAvailabilityService _availabilityService;
    private readonly IPortAllocator _portAllocator;

    public PortPlanner(
        IPortAvailabilityService availabilityService,
        IPortAllocator portAllocator)
    {
        _availabilityService = availabilityService;
        _portAllocator = portAllocator;
    }

    public async Task<GamePortPlan> CreatePlanAsync(
        GameDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Ports.Count == 0)
        {
            return new GamePortPlan(
                definition.GameId.ToString(),
                definition.DisplayName,
                PortPlanStatuses.Unknown,
                [],
                "GamesHud does not have port requirements for this game yet.");
        }

        var items = new List<GamePortPlanItem>();

        foreach (var portDefinition in definition.Ports)
        {
            var availability = await _availabilityService.CheckAvailabilityAsync(
                portDefinition.DefaultPort,
                cancellationToken);
            var allocation = await _portAllocator.AllocateAsync(
                new PortAllocationRequest(
                    new GameServerId($"{definition.GameId}-{portDefinition.Id}-preview"),
                    portDefinition.Id,
                    portDefinition.DefaultPort,
                    portDefinition.AllowAlternative,
                    CoordinateInProcess: false),
                cancellationToken);

            items.Add(new GamePortPlanItem(
                portDefinition.Id,
                portDefinition.Label,
                portDefinition.Purpose,
                portDefinition.Exposure,
                portDefinition.Required,
                portDefinition.AllowAlternative,
                availability,
                allocation));
        }

        return new GamePortPlan(
            definition.GameId.ToString(),
            definition.DisplayName,
            CalculateStatus(items),
            items,
            "Port availability is advisory until durable reservation exists.");
    }

    private static string CalculateStatus(IReadOnlyCollection<GamePortPlanItem> items)
    {
        if (items.Any(item => item.Allocation.Status == PortAllocationStatuses.Failed && item.Required))
        {
            return PortPlanStatuses.Conflict;
        }

        if (items.Any(item => item.Allocation.UsedAlternative))
        {
            return PortPlanStatuses.ReadyWithAlternatives;
        }

        return PortPlanStatuses.Ready;
    }
}
