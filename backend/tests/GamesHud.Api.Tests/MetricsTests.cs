using System.Net;
using System.Net.Http.Json;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Metrics.Contracts;
using GamesHud.Api.Metrics.Controllers;
using GamesHud.Api.Metrics.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesHud.Api.Tests;

public sealed class MetricsTests
{
    [Fact]
    public void CalculatesHostCpuPercent()
    {
        var result = MetricsCalculator.CalculateCpuPercent(
            currentTotal: 200,
            currentIdle: 120,
            previousTotal: 100,
            previousIdle: 80);

        Assert.Equal(60, result);
    }

    [Fact]
    public void CalculatesDockerCpuPercent()
    {
        var result = MetricsCalculator.CalculateDockerCpuPercent(
            currentCpuUsage: 150,
            previousCpuUsage: 100,
            currentSystemUsage: 1000,
            previousSystemUsage: 900,
            onlineCpus: 2);

        Assert.Equal(100, result);
    }

    [Fact]
    public void SubtractsContainerMemoryCacheWhenAvailable()
    {
        var result = MetricsCalculator.CalculateContainerMemoryUsage(
            1_000,
            new Dictionary<string, ulong> { ["cache"] = 200 });

        Assert.Equal<ulong>(800, result);
    }

    [Fact]
    public void HistoryStoreAppliesRetention()
    {
        var store = new InMemoryMetricsHistoryStore();
        var now = DateTimeOffset.UtcNow;

        store.Add(CreateSnapshot(now.AddHours(-2), 10), TimeSpan.FromHours(24));
        store.Add(CreateSnapshot(now, 20), TimeSpan.FromHours(1));

        var result = store.GetSince(now.AddHours(-24));

        var snapshot = Assert.Single(result);
        Assert.Equal(20, snapshot.HostCpuPercent);
    }

    [Fact]
    public void EmptyHistoryReturnsEmptyArray()
    {
        var store = new InMemoryMetricsHistoryStore();

        var result = store.GetSince(DateTimeOffset.UtcNow.AddHours(-1));

        Assert.Empty(result);
    }

    [Fact]
    public async Task SystemMetricsEndpointReturnsMappedMetricsAndHistory()
    {
        var historyStore = new InMemoryMetricsHistoryStore();
        historyStore.Add(CreateSnapshot(DateTimeOffset.UtcNow, 42), TimeSpan.FromHours(24));
        await using var factory = CreateFactory(
            new FakeMetricsService(),
            new FakePalworldMetricsService(),
            historyStore);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<SystemMetricsResponse>("/api/system/metrics?historyHours=1");

        Assert.NotNull(result);
        Assert.Equal(42, result.Host.CpuPercent);
        Assert.Equal<ulong>(4_000, result.Host.MemoryUsedBytes);
        Assert.Equal(2, result.Docker.RunningContainers);
        Assert.NotEmpty(result.History);
    }

    [Fact]
    public async Task ContainerMetricsEndpointReturnsMappedMetrics()
    {
        await using var factory = CreateFactory(
            new FakeMetricsService(),
            new FakePalworldMetricsService(),
            new InMemoryMetricsHistoryStore());
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerMetricsResponse>("/api/containers/abc123/metrics");

        Assert.NotNull(result);
        Assert.Equal("abc123", result.ContainerId);
        Assert.Equal(33.3, result.CpuPercent);
        Assert.Equal((ulong?)512, result.MemoryUsageBytes);
        Assert.Equal(50, result.MemoryPercent);
    }

    [Fact]
    public async Task PalworldMetricsEndpointReturnsMetricsAndHistory()
    {
        var historyStore = new InMemoryMetricsHistoryStore();
        historyStore.Add(CreateSnapshot(DateTimeOffset.UtcNow, 42), TimeSpan.FromHours(24));
        await using var factory = CreateFactory(
            new FakeMetricsService(),
            new FakePalworldMetricsService(),
            historyStore);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<PalworldMetricsResponse>("/api/palworld/metrics?historyHours=1");

        Assert.NotNull(result);
        Assert.Equal("palworld-server", result.ContainerName);
        Assert.Equal(21.5, result.CpuPercent);
        Assert.Equal(3, result.PlayersOnline);
        Assert.NotEmpty(result.History);
    }

    [Fact]
    public async Task SystemMetricsEndpointReturnsUnavailableWhenDockerFails()
    {
        await using var factory = CreateFactory(
            new DockerUnavailableMetricsService(),
            new FakePalworldMetricsService(),
            new InMemoryMetricsHistoryStore());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/system/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SystemMetricsEndpointReturnsUnavailableWhenHostMetricsFail()
    {
        await using var factory = CreateFactory(
            new HostUnavailableMetricsService(),
            new FakePalworldMetricsService(),
            new InMemoryMetricsHistoryStore());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/system/metrics");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SystemMetricsControllerPropagatesCancellation()
    {
        var metricsService = new FakeMetricsService();
        var controller = new SystemMetricsController(
            metricsService,
            new InMemoryMetricsHistoryStore(),
            NullLogger<SystemMetricsController>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        await controller.GetSystemMetrics(null, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, metricsService.LastCancellationToken);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IMetricsService metricsService,
        IPalworldMetricsService palworldMetricsService,
        IMetricsHistoryStore historyStore)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped(_ => metricsService);
                    services.AddScoped(_ => palworldMetricsService);
                    services.AddSingleton(historyStore);
                });
            });
    }

    private static MetricSnapshot CreateSnapshot(DateTimeOffset timestamp, double cpuPercent)
    {
        return new MetricSnapshot(
            timestamp,
            cpuPercent,
            4_000,
            8_000,
            10_000,
            20_000,
            21.5,
            512,
            1_024,
            3);
    }

    private class FakeMetricsService : IMetricsService
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public virtual Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;

            return Task.FromResult(new HostMetrics(
                42,
                4_000,
                8_000,
                10_000,
                20_000,
                3_600,
                DateTimeOffset.UtcNow));
        }

        public virtual Task<DockerSummaryMetrics> GetDockerSummaryMetricsAsync(CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;

            return Task.FromResult(new DockerSummaryMetrics(2, 1));
        }

        public virtual Task<ContainerMetrics?> GetContainerMetricsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;

            return Task.FromResult<ContainerMetrics?>(new ContainerMetrics(
                containerId,
                "gameshud-api",
                33.3,
                512,
                1_024,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakePalworldMetricsService : IPalworldMetricsService
    {
        public Task<PalworldMetrics> GetPalworldMetricsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PalworldMetrics(
                "palworld-server",
                21.5,
                512,
                1_024,
                7_200,
                3,
                10,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class DockerUnavailableMetricsService : FakeMetricsService
    {
        public override Task<DockerSummaryMetrics> GetDockerSummaryMetricsAsync(CancellationToken cancellationToken)
        {
            throw new DockerUnavailableException("Docker unavailable.");
        }
    }

    private sealed class HostUnavailableMetricsService : FakeMetricsService
    {
        public override Task<HostMetrics> GetHostMetricsAsync(CancellationToken cancellationToken)
        {
            throw new HostMetricsUnavailableException("Host unavailable.");
        }
    }
}
