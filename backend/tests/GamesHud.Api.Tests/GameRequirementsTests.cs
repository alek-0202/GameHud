using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.HostCapabilities.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GamesHud.Api.Tests;

public sealed class GameRequirementsTests
{
    [Fact]
    public void PalworldRequirementsAreRegisteredFromDocumentedServerGuideValues()
    {
        var definition = new PalworldGameDefinition();

        Assert.Equal(["linux", "windows"], definition.Requirements.SupportedOperatingSystems);
        Assert.Equal(["x64"], definition.Requirements.SupportedArchitectures);
        Assert.Equal([GameServerRuntime.DockerType], definition.Requirements.RequiredRuntimes);
        Assert.Equal(4, definition.Requirements.RecommendedLogicalProcessors);
        Assert.Equal(GameRequirementBytes.Gibibytes(8), definition.Requirements.Memory?.MinimumBytes);
        Assert.Equal(GameRequirementBytes.Gibibytes(16), definition.Requirements.Memory?.RecommendedBytes);
        Assert.Null(definition.Requirements.Storage);
        Assert.Equal(
            "https://docs.palworldgame.com/getting-started/requirements/",
            definition.Requirements.Source);
    }

    [Fact]
    public void CompatibleHostPassesAllDeclaredRequirements()
    {
        var assessment = Evaluate(CreateDefinition(), CreateHost());

        Assert.Equal(GameCompatibilityStatuses.Compatible, assessment.Status);
        Assert.All(assessment.Checks, check =>
            Assert.Equal(RequirementCheckStatuses.Passed, check.Status));
        Assert.Empty(assessment.BlockingIssues);
        Assert.Empty(assessment.Warnings);
    }

    [Fact]
    public void MinimumMemoryExactlyMetPassesWhenNoRecommendationExists()
    {
        var definition = CreateDefinition(memory: new ByteRequirement(
            minimumBytes: GameRequirementBytes.Gibibytes(8)));
        var assessment = Evaluate(definition, CreateHost(memoryTotalBytes: GameRequirementBytes.Gibibytes(8)));

        Assert.Equal(GameCompatibilityStatuses.Compatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Passed, FindCheck(assessment, "memory").Status);
    }

    [Fact]
    public void MemoryBelowMinimumIsBlocking()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(memoryTotalBytes: GameRequirementBytes.Gibibytes(4)));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Failed, FindCheck(assessment, "memory").Status);
        Assert.Contains(assessment.BlockingIssues, issue => issue.Code == "memory_failed");
    }

    [Fact]
    public void MemoryAboveMinimumButBelowRecommendedIsWarning()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(memoryTotalBytes: GameRequirementBytes.Gibibytes(12)));

        Assert.Equal(GameCompatibilityStatuses.CompatibleWithWarnings, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Warning, FindCheck(assessment, "memory").Status);
        Assert.Contains(assessment.Warnings, issue => issue.Code == "memory_warning");
    }

    [Fact]
    public void StorageBelowMinimumIsBlockingWhenStorageRequirementIsDeclared()
    {
        var assessment = Evaluate(
            CreateDefinition(storage: new ByteRequirement(
                minimumBytes: GameRequirementBytes.Gibibytes(20))),
            CreateHost(storageAvailableBytes: GameRequirementBytes.Gibibytes(12)));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Failed, FindCheck(assessment, "storage").Status);
    }

    [Fact]
    public void MissingRuntimeIsBlocking()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(runtimes: []));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Failed, FindCheck(assessment, "runtime_docker").Status);
    }

    [Fact]
    public void UnsupportedArchitectureIsBlocking()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(architecture: "arm64"));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Failed, FindCheck(assessment, "architecture").Status);
    }

    [Fact]
    public void UnsupportedOperatingSystemIsBlocking()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(osFamily: "macos"));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Equal(RequirementCheckStatuses.Failed, FindCheck(assessment, "operating_system").Status);
    }

    [Fact]
    public void UnknownMemoryProducesUnknownAssessmentWithoutBlocking()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(memoryStatus: HostCapabilityStatuses.Unavailable, memoryTotalBytes: null));

        Assert.Equal(GameCompatibilityStatuses.Unknown, assessment.Status);
        Assert.Empty(assessment.BlockingIssues);
        Assert.Contains(assessment.Warnings, issue => issue.Code == "memory_unknown");
    }

    [Fact]
    public void MultipleWarningsProduceCompatibleWithWarnings()
    {
        var assessment = Evaluate(
            CreateDefinition(storage: new ByteRequirement(
                minimumBytes: GameRequirementBytes.Gibibytes(10),
                recommendedBytes: GameRequirementBytes.Gibibytes(20))),
            CreateHost(
                logicalProcessors: 2,
                memoryTotalBytes: GameRequirementBytes.Gibibytes(12),
                storageAvailableBytes: GameRequirementBytes.Gibibytes(15)));

        Assert.Equal(GameCompatibilityStatuses.CompatibleWithWarnings, assessment.Status);
        Assert.Contains(assessment.Warnings, issue => issue.Code == "cpu_warning");
        Assert.Contains(assessment.Warnings, issue => issue.Code == "memory_warning");
        Assert.Contains(assessment.Warnings, issue => issue.Code == "storage_warning");
    }

    [Fact]
    public void BlockingAndWarningProducesIncompatibleAssessment()
    {
        var assessment = Evaluate(
            CreateDefinition(),
            CreateHost(
                architecture: "arm64",
                memoryTotalBytes: GameRequirementBytes.Gibibytes(12)));

        Assert.Equal(GameCompatibilityStatuses.Incompatible, assessment.Status);
        Assert.Contains(assessment.BlockingIssues, issue => issue.Code == "architecture_failed");
        Assert.Contains(assessment.Warnings, issue => issue.Code == "memory_warning");
    }

    [Fact]
    public void DefinitionWithoutRequirementsProducesUnknownAssessment()
    {
        var definition = new GameDefinition(
            new GameId("future-game"),
            "Future Game",
            "Future game support.",
            new GameDefinitionBranding("future-game"),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview]);

        var assessment = Evaluate(definition, CreateHost());

        Assert.Equal(GameCompatibilityStatuses.Unknown, assessment.Status);
        Assert.Contains(assessment.Warnings, issue => issue.Code == "requirements_unknown");
    }

    [Fact]
    public async Task CompatibilityEndpointReturnsAssessmentForKnownGame()
    {
        await using var factory = CreateFactory(
            new GameDefinitionRegistry([new PalworldGameDefinition()]),
            CreateHost());
        using var client = factory.CreateClient();

        var assessment = await client.GetFromJsonAsync<GameCompatibilityAssessmentResponse>(
            "/api/games/palworld/compatibility");

        Assert.NotNull(assessment);
        Assert.Equal("palworld", assessment.GameId);
        Assert.Equal(GameCompatibilityStatuses.Compatible, assessment.Status);
        Assert.Contains(assessment.Checks, check => check.Id == "runtime_docker");
    }

    [Fact]
    public async Task CompatibilityEndpointReturnsNotFoundForUnknownGame()
    {
        await using var factory = CreateFactory(
            new GameDefinitionRegistry([new PalworldGameDefinition()]),
            CreateHost());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/games/unknown/compatibility");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void CompatibilityResponseDoesNotExposeSecretsOrSensitivePaths()
    {
        var assessment = Evaluate(CreateDefinition(), CreateHost());
        var json = JsonSerializer.Serialize(GameCompatibilityContractMapper.Map(assessment));

        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagedPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BackupPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContainerName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RestApi", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", json, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IGameDefinitionRegistry registry,
        HostCapabilitySnapshot hostCapabilities)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(registry);
                    services.AddScoped<IHostCapabilityService>(_ =>
                        new FakeHostCapabilityService(hostCapabilities));
                });
            });
    }

    private static GameCompatibilityAssessment Evaluate(
        GameDefinition definition,
        HostCapabilitySnapshot hostCapabilities)
    {
        return new GameRequirementEvaluator().Evaluate(definition, hostCapabilities);
    }

    private static GameCompatibilityCheck FindCheck(
        GameCompatibilityAssessment assessment,
        string checkId)
    {
        return assessment.Checks.Single(check => check.Id == checkId);
    }

    private static GameDefinition CreateDefinition(
        ByteRequirement? memory = null,
        ByteRequirement? storage = null)
    {
        return new GameDefinition(
            new GameId("test-game"),
            "Test Game",
            "Test game support.",
            new GameDefinitionBranding("test-game"),
            [GameServerRuntime.DockerType],
            [GameServerCapabilities.Overview],
            new GameRequirements(
                supportedOperatingSystems: ["linux", "windows"],
                supportedArchitectures: ["x64"],
                requiredRuntimes: [GameServerRuntime.DockerType],
                minimumLogicalProcessors: 2,
                recommendedLogicalProcessors: 4,
                memory: memory ?? new ByteRequirement(
                    minimumBytes: GameRequirementBytes.Gibibytes(8),
                    recommendedBytes: GameRequirementBytes.Gibibytes(16)),
                storage: storage));
    }

    private static HostCapabilitySnapshot CreateHost(
        string osFamily = "linux",
        string architecture = "x64",
        int logicalProcessors = 4,
        string memoryStatus = HostCapabilityStatuses.Available,
        ulong? memoryTotalBytes = null,
        ulong? storageAvailableBytes = null,
        IReadOnlyCollection<HostRuntimeInfo>? runtimes = null)
    {
        return new HostCapabilitySnapshot(
            new HostOperatingSystemInfo(osFamily, osFamily, architecture),
            new HostCpuInfo(logicalProcessors, architecture),
            new HostMemoryInfo(
                memoryStatus,
                memoryTotalBytes ?? GameRequirementBytes.Gibibytes(16),
                GameRequirementBytes.Gibibytes(10)),
            new HostStorageInfo(
                HostCapabilityStatuses.Available,
                "/",
                GameRequirementBytes.Gibibytes(100),
                storageAvailableBytes ?? GameRequirementBytes.Gibibytes(50)),
            new HostNetworkInfo(HostCapabilityStatuses.Available, 2, true, true, true),
            runtimes ?? [
                new HostRuntimeInfo(
                    GameServerRuntime.DockerType,
                    "Docker",
                    HostCapabilityStatuses.Available,
                    true,
                    true,
                    "26.1.0",
                    "linux",
                    [])
            ],
            new HostReadinessInfo(HostReadinessStatuses.Ready, "Ready."),
            []);
    }

    private sealed class FakeHostCapabilityService : IHostCapabilityService
    {
        private readonly HostCapabilitySnapshot _hostCapabilities;

        public FakeHostCapabilityService(HostCapabilitySnapshot hostCapabilities)
        {
            _hostCapabilities = hostCapabilities;
        }

        public Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_hostCapabilities);
        }
    }
}
