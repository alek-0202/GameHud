using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Docker.DotNet.Models;
using GamesHud.Api.Controllers;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GamesHud.Api.Tests;

public class ContainersEndpointTests
{
    [Fact]
    public async Task GetContainersReturnsMappedContainers()
    {
        var containers = new[]
        {
            new ContainerResponse(
                "abc123",
                "gameshud-api",
                "gameshud/api:local",
                "running",
                "Up 2 minutes")
        };

        await using var factory = CreateFactory(new FakeContainerService(containers));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerResponse[]>("/api/containers");

        Assert.NotNull(result);
        var container = Assert.Single(result);
        Assert.Equal("abc123", container.Id);
        Assert.Equal("gameshud-api", container.Name);
        Assert.Equal("gameshud/api:local", container.Image);
        Assert.Equal("running", container.State);
        Assert.Equal("Up 2 minutes", container.Status);
    }

    [Fact]
    public void DockerContainerMapperRemovesLeadingSlashFromNames()
    {
        var container = new ContainerListResponse
        {
            ID = "abc123",
            Names = new List<string> { "/gameshud-api" },
            Image = "gameshud/api:local",
            State = "running",
            Status = "Up 1 minute"
        };

        var result = DockerContainerMapper.Map(container);

        Assert.Equal("gameshud-api", result.Name);
    }

    [Fact]
    public async Task GetContainersReturnsServiceUnavailableWhenDockerIsUnavailable()
    {
        await using var factory = CreateFactory(new UnavailableContainerService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void ContainersControllerDoesNotDependDirectlyOnDockerSdk()
    {
        var constructorParameters = typeof(ContainersController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(constructorParameters, UsesDockerSdkType);
    }

    [Fact]
    public async Task GetContainersReturnsEmptyArrayWhenThereAreNoContainers()
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerResponse[]>("/api/containers");

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetContainerDetailsReturnsDetails()
    {
        var details = CreateContainerDetailsResponse();
        await using var factory = CreateFactory(new FakeContainerService(
            Array.Empty<ContainerResponse>(),
            new Dictionary<string, ContainerDetailsResponse> { [details.Id] = details },
            new Dictionary<string, ContainerLogsResponse>()));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerDetailsResponse>($"/api/containers/{details.Id}");

        Assert.NotNull(result);
        Assert.Equal(details.Id, result.Id);
        Assert.Equal("gameshud-api", result.Name);
        Assert.Equal("gameshud/api:local", result.Image);
        Assert.Equal("running", result.State);
    }

    [Fact]
    public void DockerContainerMapperMapsDetailsSafely()
    {
        var container = CreateDockerInspectResponse();

        var result = DockerContainerMapper.MapDetails(container);

        Assert.Equal("abc123", result.Id);
        Assert.Equal("gameshud-api", result.Name);
        Assert.Equal("gameshud/api:local", result.Image);
        Assert.Equal("sha256:image123", result.ImageId);
        Assert.Equal("running", result.State);
        Assert.Equal("running", result.Status);
        Assert.Equal("2026-01-02T03:04:05.0000000Z", result.CreatedAt);
        Assert.Equal("2026-01-02T03:05:00Z", result.StartedAt);
        Assert.Equal("0001-01-01T00:00:00Z", result.FinishedAt);
        Assert.Equal(2, result.RestartCount);
        Assert.Equal("linux", result.Platform);
        Assert.Equal("overlay2", result.Driver);
        Assert.Equal("GamesHud API", result.Labels["org.opencontainers.image.title"]);
        Assert.DoesNotContain("com.docker.compose.project.config_files", result.Labels.Keys);
    }

    [Fact]
    public void DockerContainerMapperDoesNotExposeEnvironmentVariables()
    {
        var container = CreateDockerInspectResponse();

        var result = DockerContainerMapper.MapDetails(container);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("EnvironmentVariables", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Env", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SENSITIVE_ENV_VALUE=redacted", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerContainerMapperMapsPorts()
    {
        var result = DockerContainerMapper.MapDetails(CreateDockerInspectResponse());

        var port = Assert.Single(result.Ports);
        Assert.Equal(8080, port.PrivatePort);
        Assert.Equal(5258, port.PublicPort);
        Assert.Equal("tcp", port.Type);
        Assert.Equal("0.0.0.0", port.HostIp);
    }

    [Fact]
    public void DockerContainerMapperMapsMounts()
    {
        var result = DockerContainerMapper.MapDetails(CreateDockerInspectResponse());

        var mount = Assert.Single(result.Mounts);
        Assert.Equal("bind", mount.Type);
        Assert.Equal("C:\\GamesHud\\data", mount.Source);
        Assert.Equal("/data", mount.Destination);
        Assert.True(mount.ReadOnly);
    }

    [Fact]
    public void DockerContainerMapperMapsNetworks()
    {
        var result = DockerContainerMapper.MapDetails(CreateDockerInspectResponse());

        var network = Assert.Single(result.Networks);
        Assert.Equal("bridge", network.Name);
        Assert.Equal("172.17.0.2", network.IpAddress);
        Assert.Equal("172.17.0.1", network.Gateway);
        Assert.Equal("02:42:ac:11:00:02", network.MacAddress);
    }

    [Fact]
    public async Task GetContainerDetailsReturnsNotFoundWhenContainerDoesNotExist()
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerDetailsReturnsServiceUnavailableWhenDockerIsUnavailable()
    {
        await using var factory = CreateFactory(new UnavailableContainerService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/abc123");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetContainerLogsReturnsLines()
    {
        var logs = new ContainerLogsResponse(
            "abc123",
            new[] { "line one", "line two" },
            "2026-01-02T03:04:05Z");
        await using var factory = CreateFactory(new FakeContainerService(
            Array.Empty<ContainerResponse>(),
            new Dictionary<string, ContainerDetailsResponse>(),
            new Dictionary<string, ContainerLogsResponse> { [logs.ContainerId] = logs }));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerLogsResponse>("/api/containers/abc123/logs?tail=100");

        Assert.NotNull(result);
        Assert.Equal(logs.ContainerId, result.ContainerId);
        Assert.Equal(logs.Lines, result.Lines);
    }

    [Fact]
    public async Task GetContainerLogsReturnsEmptyArrayWhenThereAreNoLogs()
    {
        var logs = new ContainerLogsResponse(
            "abc123",
            Array.Empty<string>(),
            "2026-01-02T03:04:05Z");
        await using var factory = CreateFactory(new FakeContainerService(
            Array.Empty<ContainerResponse>(),
            new Dictionary<string, ContainerDetailsResponse>(),
            new Dictionary<string, ContainerLogsResponse> { [logs.ContainerId] = logs }));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerLogsResponse>("/api/containers/abc123/logs");

        Assert.NotNull(result);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task GetContainerLogsAppliesDefaultTail()
    {
        var service = new FakeContainerService(
            Array.Empty<ContainerResponse>(),
            new Dictionary<string, ContainerDetailsResponse>(),
            new Dictionary<string, ContainerLogsResponse>
            {
                ["abc123"] = new ContainerLogsResponse(
                    "abc123",
                    Array.Empty<string>(),
                    "2026-01-02T03:04:05Z")
            });
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/abc123/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, service.LastRequestedTail);
        Assert.True(service.LastRequestedTimestamps);
    }

    [Fact]
    public async Task GetContainerLogsReturnsBadRequestForInvalidTail()
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/abc123/logs?tail=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerLogsReturnsBadRequestForTailAboveLimit()
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/abc123/logs?tail=2001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerLogsReturnsNotFoundWhenContainerDoesNotExist()
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/missing/logs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContainerLogsReturnsServiceUnavailableWhenDockerIsUnavailable()
    {
        await using var factory = CreateFactory(new UnavailableContainerService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/containers/abc123/logs");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ContainersControllerPassesCancellationTokenToContainerService()
    {
        var details = CreateContainerDetailsResponse();
        var service = new FakeContainerService(
            Array.Empty<ContainerResponse>(),
            new Dictionary<string, ContainerDetailsResponse> { [details.Id] = details },
            new Dictionary<string, ContainerLogsResponse>
            {
                [details.Id] = new ContainerLogsResponse(
                    details.Id,
                    Array.Empty<string>(),
                    "2026-01-02T03:04:05Z")
            });
        var controller = new ContainersController(
            service,
            NullLogger<ContainersController>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        await controller.GetContainerDetails(details.Id, cancellationTokenSource.Token);
        await controller.GetContainerLogs(details.Id, null, null, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, service.LastDetailsCancellationToken);
        Assert.Equal(cancellationTokenSource.Token, service.LastLogsCancellationToken);
    }

    [Fact]
    public async Task StartContainerReturnsSuccessForStoppedContainer()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["start:abc123"] = CreateLifecycleResponse(
            "abc123",
            "start",
            "exited",
            "running",
            "Container started.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        var result = await client.PostAsync("/api/containers/abc123/start", content: null);
        var content = await result.Content.ReadFromJsonAsync<ContainerLifecycleActionResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal("start", content.Action);
        Assert.Equal("exited", content.PreviousState);
        Assert.Equal("running", content.CurrentState);
    }

    [Fact]
    public async Task StartContainerReturnsFriendlySuccessWhenAlreadyRunning()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["start:abc123"] = CreateLifecycleResponse(
            "abc123",
            "start",
            "running",
            "running",
            "Container is already running. No change was necessary.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        var result = await client.PostAsync("/api/containers/abc123/start", content: null);
        var content = await result.Content.ReadFromJsonAsync<ContainerLifecycleActionResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Contains("already running", content.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopContainerReturnsSuccessForRunningContainer()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["stop:abc123"] = CreateLifecycleResponse(
            "abc123",
            "stop",
            "running",
            "exited",
            "Container stopped.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        var result = await client.PostAsync("/api/containers/abc123/stop", content: null);
        var content = await result.Content.ReadFromJsonAsync<ContainerLifecycleActionResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal("stop", content.Action);
        Assert.Equal("running", content.PreviousState);
        Assert.Equal("exited", content.CurrentState);
    }

    [Fact]
    public async Task StopContainerReturnsFriendlySuccessWhenAlreadyStopped()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["stop:abc123"] = CreateLifecycleResponse(
            "abc123",
            "stop",
            "exited",
            "exited",
            "Container is already stopped. No change was necessary.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        var result = await client.PostAsync("/api/containers/abc123/stop", content: null);
        var content = await result.Content.ReadFromJsonAsync<ContainerLifecycleActionResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Contains("already stopped", content.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartContainerReturnsSuccess()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["restart:abc123"] = CreateLifecycleResponse(
            "abc123",
            "restart",
            "running",
            "running",
            "Container restarted.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        var result = await client.PostAsync("/api/containers/abc123/restart", content: null);
        var content = await result.Content.ReadFromJsonAsync<ContainerLifecycleActionResponse>();

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.Equal("restart", content.Action);
    }

    [Theory]
    [InlineData("/api/containers/missing/start")]
    [InlineData("/api/containers/missing/stop")]
    [InlineData("/api/containers/missing/restart")]
    public async Task LifecycleActionsReturnNotFoundWhenContainerDoesNotExist(string url)
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/containers/abc123/start")]
    [InlineData("/api/containers/abc123/stop")]
    [InlineData("/api/containers/abc123/restart")]
    public async Task LifecycleActionsReturnServiceUnavailableWhenDockerIsUnavailable(string url)
    {
        await using var factory = CreateFactory(new UnavailableContainerService());
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/containers/abc123/stop?timeoutSeconds=0")]
    [InlineData("/api/containers/abc123/restart?timeoutSeconds=0")]
    public async Task LifecycleActionsReturnBadRequestForTimeoutBelowMinimum(string url)
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/containers/abc123/stop?timeoutSeconds=121")]
    [InlineData("/api/containers/abc123/restart?timeoutSeconds=121")]
    public async Task LifecycleActionsReturnBadRequestForTimeoutAboveMaximum(string url)
    {
        await using var factory = CreateFactory(new FakeContainerService(Array.Empty<ContainerResponse>()));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(url, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LifecycleActionsPropagateValidTimeoutToService()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["stop:abc123"] = CreateLifecycleResponse(
            "abc123",
            "stop",
            "running",
            "exited",
            "Container stopped.");
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/containers/abc123/stop?timeoutSeconds=45",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(45, service.LastLifecycleTimeoutSeconds);
    }

    [Fact]
    public async Task LifecycleActionsPropagateCancellationTokenToService()
    {
        var service = new FakeContainerService(Array.Empty<ContainerResponse>());
        service.LifecycleResults["restart:abc123"] = CreateLifecycleResponse(
            "abc123",
            "restart",
            "running",
            "running",
            "Container restarted.");
        var controller = new ContainersController(
            service,
            NullLogger<ContainersController>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        await controller.RestartContainer("abc123", 10, cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, service.LastLifecycleCancellationToken);
    }

    [Fact]
    public void ContainerServiceContractDoesNotExposeDangerousLifecycleOperations()
    {
        var methodNames = typeof(IContainerService)
            .GetMethods()
            .Select(method => method.Name);

        Assert.DoesNotContain(methodNames, name => name.Contains("Kill", StringComparison.Ordinal));
        Assert.DoesNotContain(methodNames, name => name.Contains("Remove", StringComparison.Ordinal));
        Assert.DoesNotContain(methodNames, name => name.Contains("Recreate", StringComparison.Ordinal));
    }

    [Fact]
    public void LifecycleResponseDoesNotExposeDockerSdkTypes()
    {
        var propertyTypes = typeof(ContainerLifecycleActionResponse)
            .GetProperties()
            .Select(property => property.PropertyType);

        Assert.DoesNotContain(propertyTypes, UsesDockerSdkType);
    }

    [Fact]
    public async Task UnexpectedLifecycleErrorDoesNotReturnStackTrace()
    {
        await using var factory = CreateFactory(new UnexpectedErrorContainerService());
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/containers/abc123/start", content: null);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(nameof(InvalidOperationException), content, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected test failure", content, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(IContainerService containerService)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped(_ => containerService);
                });
            });
    }

    private static bool UsesDockerSdkType(Type type)
    {
        return type.Namespace?.StartsWith("Docker.DotNet", StringComparison.Ordinal) == true;
    }

    private static ContainerDetailsResponse CreateContainerDetailsResponse()
    {
        return new ContainerDetailsResponse(
            "abc123",
            "gameshud-api",
            "gameshud/api:local",
            "sha256:image123",
            "running",
            "running",
            "2026-01-02T03:04:05Z",
            "2026-01-02T03:05:00Z",
            string.Empty,
            2,
            "linux",
            "overlay2",
            Array.Empty<ContainerPortResponse>(),
            Array.Empty<ContainerMountResponse>(),
            Array.Empty<ContainerNetworkResponse>(),
            new Dictionary<string, string>());
    }

    private static ContainerLifecycleActionResponse CreateLifecycleResponse(
        string containerId,
        string action,
        string previousState,
        string currentState,
        string message)
    {
        return new ContainerLifecycleActionResponse(
            containerId,
            action,
            true,
            message,
            previousState,
            currentState,
            "2026-01-02T03:04:05Z");
    }

    private static ContainerInspectResponse CreateDockerInspectResponse()
    {
        return new ContainerInspectResponse
        {
            ID = "abc123",
            Name = "/gameshud-api",
            Image = "sha256:image123",
            Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            State = new ContainerState
            {
                Status = "running",
                StartedAt = "2026-01-02T03:05:00Z",
                FinishedAt = "0001-01-01T00:00:00Z"
            },
            RestartCount = 2,
            Platform = "linux",
            Driver = "overlay2",
            Config = new Config
            {
                Image = "gameshud/api:local",
                Tty = false,
                Env = new List<string> { "SENSITIVE_ENV_VALUE=redacted" },
                Labels = new Dictionary<string, string>
                {
                    ["org.opencontainers.image.title"] = "GamesHud API",
                    ["com.docker.compose.project.config_files"] = "C:\\filtered\\compose.yml"
                }
            },
            Mounts = new List<MountPoint>
            {
                new()
                {
                    Type = "bind",
                    Source = "C:\\GamesHud\\data",
                    Destination = "/data",
                    RW = false
                }
            },
            NetworkSettings = new NetworkSettings
            {
                Ports = new Dictionary<string, IList<PortBinding>>
                {
                    ["8080/tcp"] = new List<PortBinding>
                    {
                        new()
                        {
                            HostIP = "0.0.0.0",
                            HostPort = "5258"
                        }
                    }
                },
                Networks = new Dictionary<string, EndpointSettings>
                {
                    ["bridge"] = new()
                    {
                        IPAddress = "172.17.0.2",
                        Gateway = "172.17.0.1",
                        MacAddress = "02:42:ac:11:00:02"
                    }
                }
            }
        };
    }

    private sealed class FakeContainerService : IContainerService
    {
        private readonly IReadOnlyCollection<ContainerResponse> _containers;
        private readonly IReadOnlyDictionary<string, ContainerDetailsResponse> _details;
        private readonly IReadOnlyDictionary<string, ContainerLogsResponse> _logs;

        public FakeContainerService(IReadOnlyCollection<ContainerResponse> containers)
            : this(
                containers,
                new Dictionary<string, ContainerDetailsResponse>(),
                new Dictionary<string, ContainerLogsResponse>())
        {
        }

        public FakeContainerService(
            IReadOnlyCollection<ContainerResponse> containers,
            IReadOnlyDictionary<string, ContainerDetailsResponse> details,
            IReadOnlyDictionary<string, ContainerLogsResponse> logs)
        {
            _containers = containers;
            _details = details;
            _logs = logs;
        }

        public int? LastRequestedTail { get; private set; }

        public bool? LastRequestedTimestamps { get; private set; }

        public Dictionary<string, ContainerLifecycleActionResponse?> LifecycleResults { get; } = new();

        public CancellationToken LastDetailsCancellationToken { get; private set; }

        public CancellationToken LastLogsCancellationToken { get; private set; }

        public CancellationToken LastLifecycleCancellationToken { get; private set; }

        public int? LastLifecycleTimeoutSeconds { get; private set; }

        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_containers);
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            LastDetailsCancellationToken = cancellationToken;

            return Task.FromResult(_details.GetValueOrDefault(containerId));
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            CancellationToken cancellationToken)
        {
            LastRequestedTail = tail;
            LastRequestedTimestamps = timestamps;
            LastLogsCancellationToken = cancellationToken;

            return Task.FromResult(_logs.GetValueOrDefault(containerId));
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            LastLifecycleCancellationToken = cancellationToken;

            return Task.FromResult(LifecycleResults.GetValueOrDefault($"start:{containerId}"));
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            LastLifecycleTimeoutSeconds = timeoutSeconds;
            LastLifecycleCancellationToken = cancellationToken;

            return Task.FromResult(LifecycleResults.GetValueOrDefault($"stop:{containerId}"));
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            LastLifecycleTimeoutSeconds = timeoutSeconds;
            LastLifecycleCancellationToken = cancellationToken;

            return Task.FromResult(LifecycleResults.GetValueOrDefault($"restart:{containerId}"));
        }
    }

    private sealed class UnavailableContainerService : IContainerService
    {
        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        private static DockerUnavailableException CreateException()
        {
            return new DockerUnavailableException(
                "Docker Engine is unavailable.",
                new InvalidOperationException("Test failure."));
        }
    }

    private sealed class UnexpectedErrorContainerService : IContainerService
    {
        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            throw CreateException();
        }

        private static InvalidOperationException CreateException()
        {
            return new InvalidOperationException("Unexpected test failure.");
        }
    }
}
