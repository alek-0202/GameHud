namespace GamesHud.Api.GameServers.Ports;

public sealed class PortAllocator : IPortAllocator
{
    public const int AlternativeSearchLimit = 10;

    private readonly IPortAvailabilityService _availabilityService;
    private readonly SemaphoreSlim _coordinationLock = new(1, 1);
    private readonly HashSet<NetworkPort> _inProcessAllocations = [];

    public PortAllocator(IPortAvailabilityService availabilityService)
    {
        _availabilityService = availabilityService;
    }

    public async Task<PortAllocationResult> AllocateAsync(
        PortAllocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _coordinationLock.WaitAsync(cancellationToken);

        try
        {
            return await AllocateWithLockAsync(request, cancellationToken);
        }
        finally
        {
            _coordinationLock.Release();
        }
    }

    private async Task<PortAllocationResult> AllocateWithLockAsync(
        PortAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var checkedPorts = new List<NetworkPort>();
        var preferredPort = request.PreferredPort;

        if (await IsAvailableCandidateAsync(
            preferredPort,
            checkedPorts,
            request.CoordinateInProcess,
            cancellationToken))
        {
            TrackInProcessAllocation(preferredPort, request.CoordinateInProcess);

            return Allocated(
                preferredPort,
                preferredPort,
                usedAlternative: false,
                checkedPorts,
                "Preferred port is available.");
        }

        if (!request.AllowAlternative)
        {
            return Failed(
                preferredPort,
                checkedPorts,
                PortErrorCodes.PortInUse,
                "Preferred port is already in use and alternatives are not allowed.");
        }

        for (var offset = 1; offset <= AlternativeSearchLimit; offset++)
        {
            var candidateNumber = preferredPort.Number + offset;

            if (candidateNumber > 65535)
            {
                break;
            }

            var candidate = new NetworkPort(candidateNumber, preferredPort.Protocol);

            if (!await IsAvailableCandidateAsync(
                candidate,
                checkedPorts,
                request.CoordinateInProcess,
                cancellationToken))
            {
                continue;
            }

            TrackInProcessAllocation(candidate, request.CoordinateInProcess);

            return Allocated(
                preferredPort,
                candidate,
                usedAlternative: true,
                checkedPorts,
                "Alternative port was selected because the preferred port is unavailable.");
        }

        return Failed(
            preferredPort,
            checkedPorts,
            PortErrorCodes.NoAlternativePort,
            "No available alternative port was found in the configured search window.");
    }

    private async Task<bool> IsAvailableCandidateAsync(
        NetworkPort port,
        ICollection<NetworkPort> checkedPorts,
        bool coordinateInProcess,
        CancellationToken cancellationToken)
    {
        checkedPorts.Add(port);

        if (coordinateInProcess && _inProcessAllocations.Contains(port))
        {
            return false;
        }

        var availability = await _availabilityService.CheckAvailabilityAsync(port, cancellationToken);

        return availability.IsAvailable;
    }

    private void TrackInProcessAllocation(NetworkPort port, bool coordinateInProcess)
    {
        if (coordinateInProcess)
        {
            _inProcessAllocations.Add(port);
        }
    }

    private static PortAllocationResult Allocated(
        NetworkPort requestedPort,
        NetworkPort allocatedPort,
        bool usedAlternative,
        IReadOnlyCollection<NetworkPort> checkedPorts,
        string message)
    {
        return new PortAllocationResult(
            requestedPort,
            allocatedPort,
            usedAlternative,
            PortAllocationStatuses.Allocated,
            null,
            message,
            checkedPorts.ToArray());
    }

    private static PortAllocationResult Failed(
        NetworkPort requestedPort,
        IReadOnlyCollection<NetworkPort> checkedPorts,
        string errorCode,
        string message)
    {
        return new PortAllocationResult(
            requestedPort,
            null,
            false,
            PortAllocationStatuses.Failed,
            errorCode,
            message,
            checkedPorts.ToArray());
    }
}
