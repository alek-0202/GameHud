using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using GamesHud.Api.HostCapabilities.Contracts;
using GamesHud.Api.HostCapabilities.Controllers;
using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.HostCapabilities.Services;
using GamesHud.Api.Metrics.Configuration;
using GamesHud.Api.Metrics.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class HostCapabilitiesTests
{
    [Theory]
    [InlineData("linux")]
    [InlineData("windows")]
    [InlineData("macos")]
    public void HostSystemInspectorMapsOperatingSystemFamilies(string family)
    {
        var inspector = new HostSystemInspector(new FakeSystemInfoProvider
        {
            Family = family,
            OSArchitectureValue = Architecture.X64,
            ProcessArchitectureValue = Architecture.Arm64,
            LogicalProcessorCountValue = 8
        });

        var result = inspector.GetSystemInfo();

        Assert.Equal(family, result.OperatingSystem.Family);
        Assert.Equal("x64", result.OperatingSystem.Architecture);
        Assert.Equal(8, result.Cpu.LogicalProcessors);
        Assert.Equal("arm64", result.Cpu.Architecture);
    }

    [Fact]
    public async Task HostResourceInspectorParsesLinuxMemoryAndStorage()
    {
        var inspector = new HostResourceInspector(
            new FakeSystemInfoProvider { Family = "linux" },
            new FakeMetricsFileSystem(
                """
                MemTotal:       8000 kB
                MemAvailable:   3000 kB
                """),
            new FakeStorageInfoProvider(new HostStorageDriveInfo("/", 100_000, 75_000)),
            Options.Create(new MetricsOptions { HostDiskPath = "/gameshud" }));

        var result = await inspector.GetResourcesAsync(CancellationToken.None);

        Assert.Equal(HostCapabilityStatuses.Available, result.Memory.Status);
        Assert.Equal<ulong>(8_192_000, result.Memory.TotalBytes!.Value);
        Assert.Equal<ulong>(3_072_000, result.Memory.AvailableBytes!.Value);
        Assert.Equal(HostCapabilityStatuses.Available, result.Storage.Status);
        Assert.Equal("/", result.Storage.ObservedRoot);
        Assert.Equal<ulong>(100_000, result.Storage.TotalBytes!.Value);
        Assert.Equal<ulong>(75_000, result.Storage.AvailableBytes!.Value);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task HostResourceInspectorRepresentsUnsupportedMemoryExplicitly()
    {
        var inspector = new HostResourceInspector(
            new FakeSystemInfoProvider { Family = "windows" },
            new FakeMetricsFileSystem(string.Empty),
            new FakeStorageInfoProvider(new HostStorageDriveInfo("C:\\", 100_000, 75_000)),
            Options.Create(new MetricsOptions { HostDiskPath = "C:\\" }));

        var result = await inspector.GetResourcesAsync(CancellationToken.None);

        Assert.Equal(HostCapabilityStatuses.Unavailable, result.Memory.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "memory_unavailable");
    }

    [Fact]
    public async Task HostRuntimeInspectorReturnsDockerAvailable()
    {
        var inspector = new HostRuntimeInspector(new FakeDockerRuntimeClient(
            new DockerRuntimeInspection(true, true, "26.1.0", "linux")));

        var runtime = Assert.Single(await inspector.GetRuntimesAsync(CancellationToken.None));

        Assert.Equal("docker", runtime.Id);
        Assert.Equal(HostCapabilityStatuses.Available, runtime.Status);
        Assert.True(runtime.EndpointConfigured);
        Assert.True(runtime.Reachable);
        Assert.Equal("26.1.0", runtime.Version);
        Assert.Empty(runtime.Issues);
    }

    [Theory]
    [InlineData(true, "unavailable", "docker_unavailable")]
    [InlineData(false, "not_configured", "docker_not_configured")]
    public async Task HostRuntimeInspectorDifferentiatesDockerUnavailableStates(
        bool endpointConfigured,
        string expectedStatus,
        string expectedIssueCode)
    {
        var inspector = new HostRuntimeInspector(new FakeDockerRuntimeClient(
            new DockerRuntimeInspection(endpointConfigured, false, null, null)));

        var runtime = Assert.Single(await inspector.GetRuntimesAsync(CancellationToken.None));

        Assert.Equal(expectedStatus, runtime.Status);
        Assert.Equal(endpointConfigured, runtime.EndpointConfigured);
        Assert.False(runtime.Reachable);
        Assert.Contains(runtime.Issues, issue => issue.Code == expectedIssueCode);
    }

    [Fact]
    public async Task HostCapabilityServiceCalculatesReadyReadinessFromFacts()
    {
        var service = CreateCapabilityService(
            storageStatus: HostCapabilityStatuses.Available,
            dockerStatus: HostCapabilityStatuses.Available,
            dockerIssues: []);

        var result = await service.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(HostReadinessStatuses.Ready, result.OverallReadiness.Status);
    }

    [Fact]
    public async Task HostCapabilityServiceCalculatesPartialWhenDockerIsNotReady()
    {
        var service = CreateCapabilityService(
            storageStatus: HostCapabilityStatuses.Available,
            dockerStatus: HostCapabilityStatuses.Unavailable,
            dockerIssues:
            [
                new HostCapabilityIssue(
                    "docker_unavailable",
                    HostCapabilityIssueSeverities.Blocking,
                    "Docker is configured but the daemon cannot be reached.")
            ]);

        var result = await service.GetCapabilitiesAsync(CancellationToken.None);

        Assert.Equal(HostReadinessStatuses.Partial, result.OverallReadiness.Status);
        Assert.Contains(result.Issues, issue => issue.Code == "docker_unavailable");
    }

    [Fact]
    public void HostNetworkInspectorMapsInterfaceFactsWithoutAddresses()
    {
        var inspector = new HostNetworkInspector(new FakeNetworkInfoProvider([
            new NetworkInterfaceInfo(true, true, true),
            new NetworkInterfaceInfo(true, false, true)
        ]));

        var result = inspector.GetNetworkInfo();

        Assert.Equal(HostCapabilityStatuses.Available, result.Status);
        Assert.Equal(2, result.InterfaceCount);
        Assert.True(result.LoopbackAvailable);
        Assert.True(result.Ipv4Available);
    }

    [Fact]
    public async Task HostCapabilitiesEndpointReturnsMappedCapabilities()
    {
        await using var factory = CreateFactory(new FakeHostCapabilityService(CreateCapabilities()));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<HostCapabilitiesResponse>("/api/system/capabilities");

        Assert.NotNull(result);
        Assert.Equal("linux", result.OperatingSystem.Family);
        Assert.Equal("docker", Assert.Single(result.Runtimes).Id);
        Assert.Equal(HostReadinessStatuses.Ready, result.OverallReadiness.Status);
    }

    [Fact]
    public void HostCapabilitiesContractDoesNotExposeSensitiveFingerprintFields()
    {
        var json = JsonSerializer.Serialize(HostCapabilitiesContractMapper.Map(CreateCapabilities()));

        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Home", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hostname", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MacAddress", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IpAddress", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unix://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npipe://", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostCapabilitiesControllerDelegatesAndPropagatesCancellation()
    {
        var service = new FakeHostCapabilityService(CreateCapabilities());
        var controller = new HostCapabilitiesController(
            service,
            NullLogger<HostCapabilitiesController>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        _ = controller.GetCapabilities(cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, service.LastCancellationToken);
    }

    private static WebApplicationFactory<Program> CreateFactory(IHostCapabilityService service)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped(_ => service);
                });
            });
    }

    private static HostCapabilityService CreateCapabilityService(
        string storageStatus,
        string dockerStatus,
        IReadOnlyCollection<HostCapabilityIssue> dockerIssues)
    {
        return new HostCapabilityService(
            new FakeSystemInspector(),
            new FakeResourceInspector(storageStatus),
            new FakeNetworkInspector(),
            new FakeRuntimeInspector(dockerStatus, dockerIssues));
    }

    private static HostCapabilitySnapshot CreateCapabilities()
    {
        return new HostCapabilitySnapshot(
            new HostOperatingSystemInfo("linux", "Linux", "x64"),
            new HostCpuInfo(4, "x64"),
            new HostMemoryInfo(HostCapabilityStatuses.Available, 8_000, 3_000),
            new HostStorageInfo(HostCapabilityStatuses.Available, "/", 100_000, 75_000),
            new HostNetworkInfo(HostCapabilityStatuses.Available, 2, true, true, true),
            [
                new HostRuntimeInfo(
                    "docker",
                    "Docker",
                    HostCapabilityStatuses.Available,
                    true,
                    true,
                    "26.1.0",
                    "linux",
                    [])
            ],
            new HostReadinessInfo(HostReadinessStatuses.Ready, "Ready to host supported game servers."),
            []);
    }

    private sealed class FakeSystemInfoProvider : IHostSystemInfoProvider
    {
        public string Family { get; init; } = "linux";

        public Architecture OSArchitectureValue { get; init; } = Architecture.X64;

        public Architecture ProcessArchitectureValue { get; init; } = Architecture.X64;

        public int LogicalProcessorCountValue { get; init; } = 4;

        public bool IsLinux => Family == "linux";

        public bool IsWindows => Family == "windows";

        public bool IsMacOS => Family == "macos";

        public string OSDescription => Family;

        public Architecture OSArchitecture => OSArchitectureValue;

        public Architecture ProcessArchitecture => ProcessArchitectureValue;

        public int LogicalProcessorCount => LogicalProcessorCountValue;
    }

    private sealed class FakeMetricsFileSystem : IHostMetricsFileSystem
    {
        private readonly string _content;

        public FakeMetricsFileSystem(string content)
        {
            _content = content;
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(_content);
        }

        public DriveInfo GetDriveInfo(string path)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeStorageInfoProvider : IHostStorageInfoProvider
    {
        private readonly HostStorageDriveInfo _driveInfo;

        public FakeStorageInfoProvider(HostStorageDriveInfo driveInfo)
        {
            _driveInfo = driveInfo;
        }

        public HostStorageDriveInfo GetDriveInfo(string path)
        {
            return _driveInfo;
        }
    }

    private sealed class FakeDockerRuntimeClient : IDockerRuntimeClient
    {
        private readonly DockerRuntimeInspection _inspection;

        public FakeDockerRuntimeClient(DockerRuntimeInspection inspection)
        {
            _inspection = inspection;
        }

        public Task<DockerRuntimeInspection> InspectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_inspection);
        }
    }

    private sealed class FakeNetworkInfoProvider : IHostNetworkInfoProvider
    {
        private readonly IReadOnlyCollection<NetworkInterfaceInfo> _interfaces;

        public FakeNetworkInfoProvider(IReadOnlyCollection<NetworkInterfaceInfo> interfaces)
        {
            _interfaces = interfaces;
        }

        public IReadOnlyCollection<NetworkInterfaceInfo> GetNetworkInterfaces()
        {
            return _interfaces;
        }
    }

    private sealed class FakeSystemInspector : IHostSystemInspector
    {
        public HostSystemInspection GetSystemInfo()
        {
            return new HostSystemInspection(
                new HostOperatingSystemInfo("linux", "Linux", "x64"),
                new HostCpuInfo(4, "x64"));
        }
    }

    private sealed class FakeResourceInspector : IHostResourceInspector
    {
        private readonly string _storageStatus;

        public FakeResourceInspector(string storageStatus)
        {
            _storageStatus = storageStatus;
        }

        public Task<HostResourceInspection> GetResourcesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new HostResourceInspection(
                new HostMemoryInfo(HostCapabilityStatuses.Available, 8_000, 3_000),
                new HostStorageInfo(_storageStatus, "/", 100_000, 75_000),
                []));
        }
    }

    private sealed class FakeNetworkInspector : IHostNetworkInspector
    {
        public HostNetworkInfo GetNetworkInfo()
        {
            return new HostNetworkInfo(HostCapabilityStatuses.Available, 2, true, true, true);
        }
    }

    private sealed class FakeRuntimeInspector : IHostRuntimeInspector
    {
        private readonly string _dockerStatus;
        private readonly IReadOnlyCollection<HostCapabilityIssue> _dockerIssues;

        public FakeRuntimeInspector(
            string dockerStatus,
            IReadOnlyCollection<HostCapabilityIssue> dockerIssues)
        {
            _dockerStatus = dockerStatus;
            _dockerIssues = dockerIssues;
        }

        public Task<IReadOnlyCollection<HostRuntimeInfo>> GetRuntimesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<HostRuntimeInfo>>(
            [
                new HostRuntimeInfo(
                    "docker",
                    "Docker",
                    _dockerStatus,
                    true,
                    _dockerStatus == HostCapabilityStatuses.Available,
                    _dockerStatus == HostCapabilityStatuses.Available ? "26.1.0" : null,
                    _dockerStatus == HostCapabilityStatuses.Available ? "linux" : null,
                    _dockerIssues)
            ]);
        }
    }

    private sealed class FakeHostCapabilityService : IHostCapabilityService
    {
        private readonly HostCapabilitySnapshot _capabilities;

        public FakeHostCapabilityService(HostCapabilitySnapshot capabilities)
        {
            _capabilities = capabilities;
        }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;

            return Task.FromResult(_capabilities);
        }
    }
}
