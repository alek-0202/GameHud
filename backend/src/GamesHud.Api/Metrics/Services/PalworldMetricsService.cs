using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Metrics.Services;

public sealed class PalworldMetricsService : IPalworldMetricsService
{
    private readonly IOptions<PalworldOptions> _options;
    private readonly IDockerMetricsService _dockerMetricsService;
    private readonly IPalworldRestService _palworldRestService;
    private readonly ILogger<PalworldMetricsService> _logger;

    public PalworldMetricsService(
        IOptions<PalworldOptions> options,
        IDockerMetricsService dockerMetricsService,
        IPalworldRestService palworldRestService,
        ILogger<PalworldMetricsService> logger)
    {
        _options = options;
        _dockerMetricsService = dockerMetricsService;
        _palworldRestService = palworldRestService;
        _logger = logger;
    }

    public async Task<PalworldMetrics> GetPalworldMetricsAsync(CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var containerMetrics = await _dockerMetricsService.GetContainerMetricsAsync(
            containerName,
            cancellationToken);
        var restMetrics = await TryGetRestMetricsAsync(cancellationToken);

        return new PalworldMetrics(
            containerName,
            containerMetrics?.CpuPercent,
            containerMetrics?.MemoryUsageBytes,
            containerMetrics?.MemoryLimitBytes,
            restMetrics?.Uptime,
            restMetrics?.CurrentPlayerNum,
            restMetrics?.MaxPlayerNum,
            DateTimeOffset.UtcNow);
    }

    private async Task<PalworldRestMetrics?> TryGetRestMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _palworldRestService.GetMetricsAsync(cancellationToken);
        }
        catch (PalworldRestException exception)
        {
            _logger.LogWarning(exception, "Palworld REST API is unavailable while reading Palworld metrics.");

            return null;
        }
    }

    private string ResolveContainerName()
    {
        var containerName = _options.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new PalworldConfigException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }
}
