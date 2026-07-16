using System.Net;
using System.Net.Http.Json;
using Docker.DotNet.Models;
using GamesHud.Api.Controllers;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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
                "palworld",
                "steamcmd/palworld:latest",
                "running",
                "Up 2 minutes")
        };

        await using var factory = CreateFactory(new FakeContainerService(containers));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<ContainerResponse[]>("/api/containers");

        Assert.NotNull(result);
        var container = Assert.Single(result);
        Assert.Equal("abc123", container.Id);
        Assert.Equal("palworld", container.Name);
        Assert.Equal("steamcmd/palworld:latest", container.Image);
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

    private sealed class FakeContainerService : IContainerService
    {
        private readonly IReadOnlyCollection<ContainerResponse> _containers;

        public FakeContainerService(IReadOnlyCollection<ContainerResponse> containers)
        {
            _containers = containers;
        }

        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_containers);
        }
    }

    private sealed class UnavailableContainerService : IContainerService
    {
        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            throw new DockerUnavailableException(
                "Docker Engine is unavailable.",
                new InvalidOperationException("Test failure."));
        }
    }
}
