using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Controllers;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GamesHud.Api.Tests;

public sealed class GameCatalogEndpointTests
{
    [Fact]
    public async Task GetGamesReturnsCatalogFromGameDefinitionRegistry()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GameCatalogResponse>("/api/games");

        Assert.NotNull(result);
        var game = Assert.Single(result.Games);
        Assert.Equal("palworld", game.Id);
        Assert.Equal("Palworld", game.DisplayName);
        Assert.Equal("palworld", game.Branding.IconKey);
        Assert.Equal([GameServerRuntime.DockerType], game.SupportedRuntimes);
        Assert.Equal(
            [
                GameServerCapabilities.Overview,
                GameServerCapabilities.Settings,
                GameServerCapabilities.Players,
                GameServerCapabilities.Backups,
                GameServerCapabilities.Update,
                GameServerCapabilities.Logs,
                GameServerCapabilities.PlayerManagement,
                GameServerCapabilities.Mods
            ],
            game.Capabilities);
    }

    [Fact]
    public async Task GetGameReturnsRequestedDefinition()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GameCatalogItemResponse>("/api/games/PALWORLD");

        Assert.NotNull(result);
        Assert.Equal("palworld", result.Id);
        Assert.Equal("Palworld", result.DisplayName);
    }

    [Fact]
    public async Task GetGamesReturnsEmptyCatalogWhenNoDefinitionsExist()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([]));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GameCatalogResponse>("/api/games");

        Assert.NotNull(result);
        Assert.Empty(result.Games);
    }

    [Fact]
    public async Task GetGameReturnsNotFoundForUnknownDefinition()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/games/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void PublicCatalogContractRemainsLimitedToStaticMetadata()
    {
        var propertyNames = typeof(GameCatalogItemResponse)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Branding", "Capabilities", "Description", "DisplayName", "Id", "SupportedRuntimes"],
            propertyNames);

        Assert.DoesNotContain(propertyNames, IsRuntimeInstanceOrSecretField);
    }

    [Fact]
    public async Task CatalogResponseDoesNotExposeSecretsPathsOrRuntimeInstances()
    {
        await using var factory = CreateFactory(new GameDefinitionRegistry([new PalworldGameDefinition()]));
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<GameCatalogResponse>("/api/games");
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagedPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BackupPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContainerName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExternalReference", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RestApi", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AllocatedPorts", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GamesControllerDoesNotDependOnPalworldOrGameServerRuntimeServices()
    {
        var constructorParameters = typeof(GamesController)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(
            constructorParameters,
            type => type.Namespace?.Contains(".Palworld", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(constructorParameters, type => type == typeof(IGameServerRegistry));
    }

    [Fact]
    public void DuplicateGameDefinitionsRemainProtectedByTheRegistry()
    {
        Assert.Throws<GameDefinitionConfigurationException>(() =>
            new GameDefinitionRegistry([
                CreateDefinition("palworld", "First"),
                CreateDefinition("PALWORLD", "Second")
            ]));
    }

    private static WebApplicationFactory<Program> CreateFactory(IGameDefinitionRegistry registry)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(registry);
                });
            });
    }

    private static bool IsRuntimeInstanceOrSecretField(string name)
    {
        return name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AllocatedPorts", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || name.Contains("RuntimeState", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ExternalReference", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rest", StringComparison.OrdinalIgnoreCase);
    }

    private static GameDefinition CreateDefinition(string gameId, string displayName)
    {
        return new GameDefinition(
            new GameId(gameId),
            displayName,
            $"Dedicated server support for {displayName}.",
            new GameDefinitionBranding(gameId.ToLowerInvariant()),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview]);
    }
}
