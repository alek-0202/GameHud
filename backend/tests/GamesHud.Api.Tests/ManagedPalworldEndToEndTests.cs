using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.Authentication;
using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.HostCapabilities.Models;
using GamesHud.Api.HostCapabilities.Services;
using GamesHud.Api.Palworld.ManagedConfiguration;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using GamesHud.Api.Secrets.Models;
using GamesHud.Api.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GameSecretReference = GamesHud.Api.Secrets.Models.SecretReference;

namespace GamesHud.Api.Tests;

public sealed class ManagedPalworldEndToEndTests
{
    private const string ApprovedDigest = "sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9";
    private const string ApprovedReference = "docker.io/thijsvanloef/palworld-server-docker@" + ApprovedDigest;
    private const string LocalImageId = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task HttpSchedulesPendingIntentAndWorkerCompletesIt()
    {
        await using var harness = await Harness.CreateAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                TestAuthentication.Add(services);
                services.AddSingleton<IManagedGameServerApplicationService>(services =>
                    new ApplicationBridge(harness.Services, services.GetRequiredService<IHttpContextAccessor>()));
                services.AddSingleton<IManagedGameServerQueryService>(new QueryBridge(harness.Services));
            }));
        using var client = factory.CreateClient();
        client.AuthenticateAs("user-a");
        var csrf = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
        { Content = JsonContent.Create(new { gameId = "palworld", displayName = "HTTP E2E",
            ownerId = "attacker", userId = "attacker", accountId = "attacker" }) };
        request.Headers.Add("Idempotency-Key", "http-worker-e2e");
        request.AddCsrf(csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateManagedGameServerResponse>();
        Assert.NotNull(created);
        Assert.Equal(0, harness.Containers.CreateCount);
        var pending = await client.GetFromJsonAsync<ManagedProvisioningResponse>(
            $"/api/game-servers/{created.GameServerId}/provisioning");
        Assert.Equal(ProvisioningOperationStatuses.Pending, pending?.Status);

        await harness.ExecuteWorkerAsync();

        var server = await client.GetFromJsonAsync<ManagedGameServerResponse>($"/api/game-servers/{created.GameServerId}");
        var completed = await client.GetFromJsonAsync<ManagedProvisioningResponse>(
            $"/api/game-servers/{created.GameServerId}/provisioning");
        Assert.Equal(ManagedGameServerLifecycleStates.Running, server?.LifecycleState);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, completed?.Status);
        Assert.Equal(1, harness.Containers.CreateCount);

        await using (var verification = harness.Services.CreateAsyncScope())
        {
            var database = verification.ServiceProvider.GetRequiredService<GamesHudDbContext>();
            Assert.Equal("user-a", (await database.ManagedGameServers.SingleAsync(
                item => item.Id == created.GameServerId)).OwnerId);
            database.ManagedGameServers.Add(new ManagedGameServerRecord
            {
                Id = "ownerless-history", GameId = "palworld", DisplayName = "Historical",
                InstallationType = ManagedInstallationTypes.Managed, RuntimeType = "docker",
                LifecycleState = ManagedGameServerLifecycleStates.Running
            });
            await database.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Remove(TestAuthentication.UserHeader);
        client.AuthenticateAs("user-b");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/game-servers/{created.GameServerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/game-servers/{created.GameServerId}/provisioning")).StatusCode);
        client.DefaultRequestHeaders.Remove(TestAuthentication.UserHeader);
        client.AuthenticateAs("user-a");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/game-servers/ownerless-history")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentHttpRetriesCreateExactlyOneDurableIntent()
    {
        await using var harness = await Harness.CreateAsync();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                TestAuthentication.Add(services);
                services.AddSingleton<IManagedGameServerApplicationService>(services =>
                    new ApplicationBridge(harness.Services, services.GetRequiredService<IHttpContextAccessor>()));
                services.AddSingleton<IManagedGameServerQueryService>(new QueryBridge(harness.Services));
            }));
        using var client = factory.CreateClient();
        client.AuthenticateAs("user-a");
        var csrf = await client.GetCsrfTokenAsync();
        HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
            { Content = JsonContent.Create(new { gameId = "palworld", displayName = "Concurrent" }) };
            request.Headers.Add("Idempotency-Key", "same-concurrent-key");
            request.AddCsrf(csrf);
            return request;
        }
        using var firstRequest = CreateRequest(); using var secondRequest = CreateRequest();
        var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response =>
            response.Content.ReadFromJsonAsync<CreateManagedGameServerResponse>()));
        Assert.Single(bodies.Select(body => body!.GameServerId).Distinct());
        Assert.Single(bodies.Select(body => body!.ProvisioningOperationId).Distinct());
        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesHudDbContext>();
        Assert.Equal(1, await db.ManagedGameServers.CountAsync());
        Assert.Equal(1, await db.ProvisioningOperations.CountAsync());
        Assert.Equal(1, await db.ManagedGameServerRequests.CountAsync());
        foreach (var response in responses) response.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ManagedPalworldRunsTheRealV2Pipeline(bool imageInitiallyAbsent, bool expectPull)
    {
        await using var harness = await Harness.CreateAsync(imageInitiallyAbsent: imageInitiallyAbsent);

        using var caller = new CancellationTokenSource();
        ProvisioningExecutionResult scheduled;
        await using (var scope = harness.Services.CreateAsyncScope())
        {
            scheduled = await scope.ServiceProvider.GetRequiredService<IGameServerProvisioningService>()
                .ScheduleProvisioningAsync(new("pal-e2e", "palworld", "E2E Palworld"), caller.Token);
        }
        caller.Cancel();

        Assert.True(scheduled.Succeeded);
        Assert.Equal(ProvisioningOperationStatuses.Pending, scheduled.Status);
        Assert.True(harness.Signal.Signalled);
        Assert.Equal(0, harness.Containers.CreateCount);
        Assert.Equal(0, harness.Images.PullCount);

        await harness.ExecuteWorkerAsync();

        await using var verification = harness.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<GamesHudDbContext>();
        var operation = await db.ProvisioningOperations.AsNoTracking().Include(item => item.Steps)
            .SingleAsync(item => item.Id == scheduled.OperationId);
        var server = await db.ManagedGameServers.AsNoTracking()
            .Include(item => item.PortReservations).Include(item => item.StorageReservations)
            .SingleAsync(item => item.Id == "pal-e2e");
        var image = await db.RuntimeImageIntents.AsNoTracking().SingleAsync(item => item.OperationId == operation.Id);
        var configuration = await db.ManagedGameConfigurations.AsNoTracking().SingleAsync(item => item.GameServerId == server.Id);

        Assert.Equal(ProvisioningPipelines.ImageAcquisitionVersion, operation.PipelineVersion);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation.Status);
        Assert.Null(operation.ActiveSlot);
        Assert.All(operation.Steps, step => Assert.Equal(ProvisioningStepStatuses.Succeeded, step.Status));
        Assert.Equal(ManagedGameServerLifecycleStates.Running, server.LifecycleState);
        Assert.Equal(LocalImageId, image.VerifiedLocalImageId);
        Assert.Equal("docker.io", image.Registry);
        Assert.Equal("thijsvanloef/palworld-server-docker", image.Repository);
        Assert.Equal(ApprovedDigest, image.ApprovedDigest);
        Assert.Equal("linux", image.PlatformOs);
        Assert.Equal("amd64", image.PlatformArchitecture);
        Assert.Equal(expectPull ? 2 : 1, harness.Images.InspectCount);
        Assert.Equal(expectPull ? 1 : 0, harness.Images.PullCount);
        Assert.Equal(ApprovedReference, Assert.Single(harness.Images.InspectedReferences.Distinct()));
        if (expectPull)
        {
            Assert.Equal(ApprovedReference, Assert.Single(harness.Images.PulledReferences));
            Assert.Equal("linux/amd64", Assert.Single(harness.Images.PulledPlatforms));
        }

        var storage = Assert.Single(server.StorageReservations);
        Assert.StartsWith(harness.ApiRoot, storage.ApiPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(harness.HostRoot, storage.HostPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(storage.ApiPath, storage.HostPath);
        Assert.True(Directory.Exists(storage.ApiPath));
        var iniPath = Path.Combine(storage.ApiPath, "Pal", "Saved", "Config", "LinuxServer", "PalWorldSettings.ini");
        var ini = await File.ReadAllTextAsync(iniPath);
        Assert.StartsWith("[/Script/Pal.PalGameWorldSettings]", ini, StringComparison.Ordinal);
        Assert.Contains("ServerName=\"E2E Palworld\"", ini, StringComparison.Ordinal);
        Assert.Contains("ServerPlayerMaxNum=32", ini, StringComparison.Ordinal);
        Assert.Contains("Difficulty=None", ini, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.SecretPlaintext, configuration.Payload, StringComparison.Ordinal);

        var created = Assert.Single(harness.Containers.Created);
        Assert.Equal(LocalImageId, created.Image);
        Assert.Equal("gameshud-pal-e2e", created.Name);
        Assert.Equal("true", created.Labels["gameshud.managed"]);
        Assert.Equal("pal-e2e", created.Labels["gameshud.gameServerId"]);
        Assert.Equal("palworld", created.Labels["gameshud.gameId"]);
        Assert.Equal(["DISABLE_GENERATE_SETTINGS=true"], created.Env);
        Assert.Equal($"{storage.HostPath}:/palworld:rw", Assert.Single(created.HostConfig.Binds));
        Assert.False(created.HostConfig.Privileged);
        Assert.Equal("default", created.HostConfig.NetworkMode);
        Assert.Equal(RestartPolicyKind.UnlessStopped, created.HostConfig.RestartPolicy.Name);
        Assert.Equal(1_000_000_000L, created.HostConfig.NanoCPUs);
        Assert.Equal(8L * 1024 * 1024 * 1024, created.HostConfig.Memory);
        Assert.Null(created.Cmd);
        Assert.Null(created.Entrypoint);
        Assert.Equal(1, harness.Containers.CreateCount);
        Assert.Equal(1, harness.Containers.StartCount);

        Assert.Contains("8211/udp", created.ExposedPorts.Keys);
        Assert.Contains("27015/udp", created.ExposedPorts.Keys);
        Assert.Contains("8212/tcp", created.ExposedPorts.Keys);
        var game = server.PortReservations.Single(item => item.PortDefinitionId == "game");
        Assert.Equal(8211, game.ContainerPort);
        Assert.Equal(harness.Collision ? 8212 : 8211, game.HostPort);
        Assert.Equal(game.HostPort!.Value.ToString(), Assert.Single(created.HostConfig.PortBindings["8211/udp"]).HostPort);
        Assert.Equal("27015", Assert.Single(created.HostConfig.PortBindings["27015/udp"]).HostPort);
        Assert.DoesNotContain("8212/tcp", created.HostConfig.PortBindings.Keys);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("admin")]
    public async Task SecretReferencesSurviveSchedulingAndAreResolvedOnlyDuringConfiguration(string kind)
    {
        await using var harness = await Harness.CreateAsync(secretKind: kind);
        var scheduled = await harness.ScheduleAsync("pal-secret");

        await harness.ExecuteWorkerAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesHudDbContext>();
        var config = await db.ManagedGameConfigurations.AsNoTracking().SingleAsync(item => item.GameServerId == "pal-secret");
        var operation = await db.ProvisioningOperations.AsNoTracking().SingleAsync(item => item.Id == scheduled.OperationId);
        var image = await db.RuntimeImageIntents.AsNoTracking().SingleAsync(item => item.OperationId == scheduled.OperationId);
        var storage = await db.StorageReservations.AsNoTracking().SingleAsync(item => item.GameServerId == "pal-secret");
        var ini = await File.ReadAllTextAsync(Path.Combine(storage.ApiPath, "Pal", "Saved", "Config", "LinuxServer", "PalWorldSettings.ini"));

        Assert.Contains(harness.SecretReference!.Id.Value, config.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.SecretPlaintext, config.Payload, StringComparison.Ordinal);
        Assert.Contains(harness.SecretPlaintext, ini, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.SecretPlaintext, operation.ErrorMessageSafe ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.SecretPlaintext,
            $"{image.Registry}/{image.Repository}@{image.ApprovedDigest}", StringComparison.Ordinal);
        Assert.Equal(1, harness.Secrets.ResolveCount);
    }

    [Fact]
    public async Task Arm64DaemonIsRejectedBeforeAnySideEffect()
    {
        await using var harness = await Harness.CreateAsync(daemonArchitecture: "arm64");

        var result = await harness.ScheduleAsync("pal-arm64");

        Assert.False(result.Succeeded);
        Assert.Equal(ProvisioningErrorCodes.HostIncompatible, result.Failure!.Code);
        Assert.Equal(0, harness.Images.InspectCount);
        Assert.Equal(0, harness.Images.PullCount);
        Assert.Equal(0, harness.Containers.CreateCount);
        Assert.Equal(0, harness.Containers.StartCount);
        Assert.Empty(Directory.EnumerateDirectories(harness.ApiRoot));
    }

    [Fact]
    public async Task HostPortCollisionKeepsPalworldContainerPortsStable()
    {
        await using var harness = await Harness.CreateAsync(collision: true);
        await harness.ScheduleAsync("pal-collision");
        await harness.ExecuteWorkerAsync();

        var created = Assert.Single(harness.Containers.Created);
        Assert.Equal("8212", Assert.Single(created.HostConfig.PortBindings["8211/udp"]).HostPort);
        Assert.Contains("8211/udp", created.ExposedPorts.Keys);
        Assert.DoesNotContain(created.Env, value => value.StartsWith("PORT=", StringComparison.Ordinal)
            || value.StartsWith("QUERY_PORT=", StringComparison.Ordinal)
            || value.StartsWith("REST_API_PORT=", StringComparison.Ordinal));
    }

    [Fact]
    public void PalworldUsesAConservativeGameSpecificHealthWindow()
    {
        var options = new RuntimeHealthOptions();
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(600, options.PalworldTimeoutSeconds);
        Assert.InRange(options.PalworldTimeoutSeconds, 1, RuntimeHealthOptions.MaximumGameTimeoutSeconds);
    }

    [Fact]
    public void PalworldKeepsOfficialMinimumAndRecommendationDistinct()
    {
        var requirements = new PalworldGameDefinition().Requirements;
        Assert.Null(requirements.MinimumLogicalProcessors);
        Assert.Equal(4, requirements.RecommendedLogicalProcessors);
        Assert.Equal(8UL * 1024 * 1024 * 1024, requirements.Memory!.MinimumBytes);
        Assert.Equal(16UL * 1024 * 1024 * 1024, requirements.Memory.RecommendedBytes);
    }

    [Theory]
    [InlineData("after_configure")]
    [InlineData("after_pull")]
    [InlineData("after_create")]
    [InlineData("after_start")]
    public async Task RestartReconcilesProvenEffectsAndCompletes(string interruption)
    {
        await using var harness = await Harness.CreateAsync(imageInitiallyAbsent: interruption == "after_pull");
        harness.Images.FailNextInspect = interruption == "after_configure";
        harness.Images.FailFinalInspectAfterPull = interruption == "after_pull";
        harness.Containers.FailCreateAfterDispatch = interruption == "after_create";
        harness.Containers.FailStartAfterDispatch = interruption == "after_start";
        var scheduled = await harness.ScheduleAsync("pal-recovery");

        await harness.ExecuteWorkerAsync();
        await using (var interruptedScope = harness.Services.CreateAsyncScope())
        {
            var interrupted = await interruptedScope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
                .GetAsync(scheduled.OperationId!);
            Assert.Equal(ProvisioningOperationStatuses.Failed, interrupted!.Status);
            Assert.True(interrupted.IsActive);
        }

        await harness.ExecuteWorkerAsync();

        await using var recoveredScope = harness.Services.CreateAsyncScope();
        var recovered = await recoveredScope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        var server = await recoveredScope.ServiceProvider.GetRequiredService<IManagedServerStore>()
            .GetManagedServerAsync("pal-recovery");
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, recovered!.Status);
        Assert.False(recovered.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.Running, server!.LifecycleState);
        Assert.Equal(1, harness.Containers.CreateCount);
        Assert.Equal(1, harness.Containers.StartCount);
        Assert.True(recovered.Steps.Single(step => step.StepId == interruption switch
        {
            "after_configure" or "after_pull" => ProvisioningStepIds.AcquireImage,
            "after_create" => ProvisioningStepIds.CreateRuntime,
            _ => ProvisioningStepIds.StartRuntime
        }).Attempt >= 1);
    }

    [Fact]
    public async Task AmbiguousDispatchedCreateBlocksWithoutBlindRetry()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Containers.AmbiguousCreateAfterDispatch = true;
        var scheduled = await harness.ScheduleAsync("pal-ambiguous");

        await harness.ExecuteWorkerAsync();
        await harness.ExecuteWorkerAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var operation = await scope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        var server = await scope.ServiceProvider.GetRequiredService<IManagedServerStore>()
            .GetManagedServerAsync("pal-ambiguous");
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation!.Status);
        Assert.True(operation.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.ProvisioningBlocked, server!.LifecycleState);
        Assert.Equal(1, harness.Containers.CreateCount);
        Assert.Equal(0, harness.Containers.StartCount);
    }

    [Fact]
    public async Task KnownDigestFailureEndsFailedAndReleasesTheSlot()
    {
        await using var harness = await Harness.CreateAsync(imageInitiallyAbsent: true);
        harness.Images.PullResult = DockerImagePullStatuses.DigestUnavailable;
        var scheduled = await harness.ScheduleAsync("pal-known-failure");

        await harness.ExecuteWorkerAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var operation = await scope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        var server = await scope.ServiceProvider.GetRequiredService<IManagedServerStore>()
            .GetManagedServerAsync("pal-known-failure");
        Assert.Equal(ProvisioningOperationStatuses.Failed, operation!.Status);
        Assert.False(operation.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.ProvisioningFailed, server!.LifecycleState);
        Assert.Equal(0, harness.Containers.CreateCount);
        Assert.Equal(0, harness.Containers.StartCount);
    }

    [Fact]
    public async Task SignalAndLaterDiscoveryDoNotDuplicateRuntimeMutations()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.ScheduleAsync("pal-wakeup");

        await harness.ExecuteWorkerAsync();
        await harness.ExecuteWorkerAsync();

        Assert.True(harness.Signal.Signalled);
        Assert.Equal(1, harness.Containers.CreateCount);
        Assert.Equal(1, harness.Containers.StartCount);
    }

    [Fact]
    public async Task StartupDiscoveryFinalizesAnOperationWhoseStepsAreAlreadySucceeded()
    {
        await using var harness = await Harness.CreateAsync();
        var scheduled = await harness.ScheduleAsync("pal-finalize");
        await using (var scope = harness.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesHudDbContext>();
            var images = scope.ServiceProvider.GetRequiredService<IRuntimeImageIntentStore>();
            var owner = new RuntimeImageOwner(scheduled.OperationId!, "pal-finalize", "palworld", "docker");
            var record = await db.ProvisioningOperations.Include(item => item.Steps)
                .SingleAsync(item => item.Id == scheduled.OperationId);
            foreach (var step in record.Steps.Where(step => step.Sequence is 4 or 5))
            {
                step.Status = ProvisioningStepStatuses.Succeeded;
                step.Attempt = 1;
            }
            var acquisition = record.Steps.Single(step => step.StepId == ProvisioningStepIds.AcquireImage);
            acquisition.Status = ProvisioningStepStatuses.Running;
            acquisition.Attempt = 1;
            record.Status = ProvisioningOperationStatuses.Running;
            record.CurrentStep = ProvisioningStepIds.AcquireImage;
            record.Version++;
            await db.SaveChangesAsync();
            var intent = await images.LoadAsync(owner);
            await images.RecordVerifiedAsync(owner, new PalworldGameDefinition().RuntimeImages.Single(),
                new GamesHud.Api.GameServers.Definitions.LocalImageId(LocalImageId), intent.Version);
            foreach (var step in record.Steps)
            {
                step.Status = ProvisioningStepStatuses.Succeeded;
                step.Attempt = Math.Max(step.Attempt, 1);
            }
            record.Status = ProvisioningOperationStatuses.Running;
            record.CurrentStep = ProvisioningStepIds.Complete;
            record.Version++;
            await db.SaveChangesAsync();
        }

        await harness.ExecuteWorkerAsync();

        await using var verification = harness.Services.CreateAsyncScope();
        var operation = await verification.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        var server = await verification.ServiceProvider.GetRequiredService<IManagedServerStore>()
            .GetManagedServerAsync("pal-finalize");
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation!.Status);
        Assert.False(operation.IsActive);
        Assert.Equal(ManagedGameServerLifecycleStates.Running, server!.LifecycleState);
        Assert.Equal(0, harness.Containers.CreateCount);
    }

    [Fact]
    public void HistoricalPipelineRemainsNineStepV1WithoutImageAcquisition()
    {
        Assert.Equal(ProvisioningPipeline.Version, ProvisioningPipelines.Legacy.Version);
        Assert.Equal(9, ProvisioningPipelines.Legacy.Steps.Count);
        Assert.DoesNotContain(ProvisioningPipelines.Legacy.Steps, step => step.Id == ProvisioningStepIds.AcquireImage);
    }

    [Fact]
    public async Task PalworldHealthUsesItsLongWindowWithoutRealDelays()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Containers.StartingHealthInspections = 32;
        var scheduled = await harness.ScheduleAsync("pal-slow-health");

        await harness.ExecuteWorkerAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var operation = await scope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation!.Status);
        Assert.Equal(31, harness.HealthDelay.WaitCount);
    }

    [Fact]
    public async Task RestartDuringHealthRetriesTheReadOnlyStepAndCompletes()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Containers.StartingHealthInspections = 2;
        harness.HealthDelay.InterruptNextWait = true;
        var scheduled = await harness.ScheduleAsync("pal-health-restart");

        await harness.ExecuteWorkerAsync();
        await harness.ExecuteWorkerAsync();

        await using var scope = harness.Services.CreateAsyncScope();
        var operation = await scope.ServiceProvider.GetRequiredService<IProvisioningOperationStore>()
            .GetAsync(scheduled.OperationId!);
        Assert.Equal(ProvisioningOperationStatuses.Succeeded, operation!.Status);
        Assert.Equal(2, operation.Steps.Single(step => step.StepId == ProvisioningStepIds.VerifyHealth).Attempt);
        Assert.Equal(1, harness.Containers.CreateCount);
        Assert.Equal(1, harness.Containers.StartCount);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _provider;

        private Harness(string root, ServiceProvider provider, FakeDockerImages images,
            FakeDockerRuntime containers, TrackingSignal signal, FakeSecretStore secrets, FakeHealthDelay healthDelay,
            string apiRoot, string hostRoot, bool collision, GameSecretReference? secretReference)
        {
            _root = root;
            _provider = provider;
            Images = images;
            Containers = containers;
            Signal = signal;
            Secrets = secrets;
            HealthDelay = healthDelay;
            ApiRoot = apiRoot;
            HostRoot = hostRoot;
            Collision = collision;
            SecretReference = secretReference;
        }

        public IServiceProvider Services => _provider;
        public FakeDockerImages Images { get; }
        public FakeDockerRuntime Containers { get; }
        public TrackingSignal Signal { get; }
        public FakeSecretStore Secrets { get; }
        public FakeHealthDelay HealthDelay { get; }
        public string ApiRoot { get; }
        public string HostRoot { get; }
        public bool Collision { get; }
        public GameSecretReference? SecretReference { get; }
        public string SecretPlaintext => "e2e-secret-value";

        public static async Task<Harness> CreateAsync(bool imageInitiallyAbsent = false, bool collision = false,
            string daemonArchitecture = "amd64", string? secretKind = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "gameshud-gh16", Guid.NewGuid().ToString("N"));
            var apiRoot = Path.Combine(root, "api-managed");
            var hostRoot = Path.Combine(root, "docker-host-managed");
            Directory.CreateDirectory(apiRoot);
            Directory.CreateDirectory(hostRoot);
            var images = new FakeDockerImages(imageInitiallyAbsent);
            var containers = new FakeDockerRuntime();
            var signal = new TrackingSignal();
            var secrets = new FakeSecretStore();
            var healthDelay = new FakeHealthDelay();
            GameSecretReference? secretReference = null;
            if (secretKind is not null)
            {
                secretReference = await secrets.StoreAsync(new(secretKind == "server"
                    ? SecretPurpose.GameServerPassword : SecretPurpose.GameAdminPassword), SecretValue.FromPlainText("e2e-secret-value"));
            }
            var codec = new PalworldProvisioningConfigurationCodec();
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddDbContext<GamesHudDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(root, "gameshud.db")};Pooling=False"));
            services.AddScoped<IPersistenceTransactionBoundary, EfCorePersistenceTransactionBoundary>();
            services.AddScoped<IManagedServerStore, ManagedServerStore>();
            services.AddSingleton<IProvisioningStateMachine, ProvisioningStateMachine>();
            services.AddScoped<IProvisioningOperationStore, ProvisioningOperationStore>();
            services.AddScoped<IProvisioningRecoveryService, ProvisioningRecoveryService>();
            services.AddScoped<IProvisioningReconciliationService, ProvisioningReconciliationService>();
            services.AddScoped<IProvisioningContextLoader, ProvisioningContextLoader>();
            services.AddScoped<IProvisioningOperationExecutor, ProvisioningOperationExecutor>();
            services.AddSingleton<IProvisioningExecutionSignal>(signal);
            services.AddSingleton(new PalworldGameDefinition());
            services.AddSingleton<GameDefinition>(sp => sp.GetRequiredService<PalworldGameDefinition>());
            services.AddSingleton<IGameDefinitionRegistry, GameDefinitionRegistry>();
            services.AddSingleton<IHostCapabilityService>(new FakeHostCapabilities(daemonArchitecture));
            services.AddSingleton<IGameRequirementEvaluator, GameRequirementEvaluator>();
            services.AddSingleton<IPortAvailabilityService>(new FakePortAvailability(collision));
            services.AddSingleton<IPortAllocator, PortAllocator>();
            services.AddScoped<IPortPlanner, PortPlanner>();
            services.AddSingleton<IManagedStoragePathBuilder>(new ManagedStoragePathBuilder(Options.Create(new StorageOptions
            {
                DataRoot = apiRoot,
                ManagedApiRoot = apiRoot,
                ManagedHostRoot = hostRoot
            })));
            services.AddSingleton<IHostStorageInfoProvider, FakeStorageInfo>();
            services.AddScoped<IGameStoragePlanner, GameStoragePlanner>();
            services.AddScoped<IManagedStorageTargetBuilder, ManagedStorageTargetBuilder>();
            services.AddSingleton<IManagedDirectoryOperations, SystemManagedDirectoryOperations>();
            services.AddSingleton<IManagedStorageProvider, ManagedStorageProvider>();
            services.AddSingleton(codec);
            services.AddSingleton<IGameProvisioningConfigurationCodec>(new InitialCodec(codec,
                secretKind == "server" ? secretReference : null, secretKind == "admin" ? secretReference : null));
            services.AddScoped<IGameProvisioningConfigurationStore, GameProvisioningConfigurationStore>();
            services.AddScoped<IRuntimeImageIntentStore, RuntimeImageIntentStore>();
            services.AddScoped<IProvisioningPlanBuilder, ProvisioningPlanBuilder>();
            services.AddScoped<IGameServerProvisioningService, GameServerProvisioningService>();
            services.AddScoped<IManagedGameServerApplicationService, ManagedGameServerApplicationService>();
            services.AddScoped<IManagedGameServerQueryService, ManagedGameServerQueryService>();
            services.AddSingleton<GamesHud.Api.Authentication.ICurrentUser>(
                new FixedCurrentUser("user-a"));
            services.AddScoped<IProvisioningEngine, ProvisioningEngine>();
            services.AddSingleton<ISecretStore>(secrets);
            services.AddScoped<IPalworldManagedConfigurationIntentReader, PalworldManagedConfigurationIntentReader>();
            services.AddScoped<IPalworldManagedConfigurationTargetBuilder, PalworldManagedConfigurationTargetBuilder>();
            services.AddSingleton<IPalworldManagedConfigurationSerializer, PalworldManagedConfigurationSerializer>();
            services.AddSingleton<IPalworldManagedConfigurationFileSystem, SystemPalworldManagedConfigurationFileSystem>();
            services.AddSingleton<IPalworldManagedConfigurationFileStore, PalworldManagedConfigurationFileStore>();
            services.AddSingleton<IRuntimeMutationPolicy, RuntimeMutationPolicy>();
            services.AddScoped<IRuntimeSpecificationBuilder, RuntimeSpecificationBuilder>();
            services.AddScoped<IRuntimeReconciliationSpecificationBuilder>(sp =>
                (RuntimeSpecificationBuilder)sp.GetRequiredService<IRuntimeSpecificationBuilder>());
            services.AddSingleton<IDockerImageAcquisitionClient>(images);
            services.AddSingleton<IRuntimeImageAcquisitionAdapter, DockerRuntimeImageAcquisitionAdapter>();
            services.AddSingleton<IDockerManagedRuntimeClient>(containers);
            services.AddSingleton<IManagedRuntimeStorageValidator, ManagedRuntimeStorageValidator>();
            services.AddSingleton<DockerGameRuntimeAdapter>();
            services.AddSingleton<IGameRuntimeAdapter>(sp => sp.GetRequiredService<DockerGameRuntimeAdapter>());
            services.AddSingleton<IManagedRuntimeInspector>(sp => sp.GetRequiredService<DockerGameRuntimeAdapter>());
            services.AddScoped<IRuntimeMutationExecutor, RuntimeMutationExecutor>();
            services.AddSingleton<IRuntimeHealthDelay>(healthDelay);
            services.AddSingleton<IOptions<RuntimeImageAcquisitionOptions>>(Options.Create(new RuntimeImageAcquisitionOptions()));
            services.AddSingleton<IOptions<RuntimeHealthOptions>>(Options.Create(new RuntimeHealthOptions()));
            services.AddScoped<IProvisioningStep, PrepareStorageProvisioningStep>();
            services.AddScoped<IProvisioningStep, ConfigurePalworldGameProvisioningStep>();
            services.AddScoped<IProvisioningStep, AcquireImageProvisioningStep>();
            services.AddScoped<IProvisioningStep, CreateRuntimeProvisioningStep>();
            services.AddScoped<IProvisioningStep, StartRuntimeProvisioningStep>();
            services.AddScoped<IProvisioningStep, VerifyRuntimeHealthProvisioningStep>();
            services.AddScoped<IProvisioningStepReconciler, PrepareStorageReconciler>();
            services.AddScoped<IProvisioningStepReconciler, ConfigurePalworldGameReconciler>();
            services.AddScoped<IProvisioningStepReconciler, AcquireImageReconciler>();
            services.AddScoped<IProvisioningStepReconciler, CreateRuntimeReconciler>();
            services.AddScoped<IProvisioningStepReconciler, StartRuntimeReconciler>();
            foreach (var id in ProvisioningStepIds.ExecutableFoundation.Where(id => id is not ProvisioningStepIds.PrepareStorage
                and not ProvisioningStepIds.ConfigureGame and not ProvisioningStepIds.AcquireImage
                and not ProvisioningStepIds.CreateRuntime and not ProvisioningStepIds.StartRuntime
                and not ProvisioningStepIds.VerifyHealth))
                services.AddScoped<IProvisioningStep>(_ => new NoHostMutationProvisioningStep(id));

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await using (var scope = provider.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<GamesHudDbContext>();
                await database.Database.MigrateAsync();
                database.Users.Add(new ApplicationUser
                {
                    Id = "user-a", UserName = "user-a@test.invalid",
                    NormalizedUserName = "USER-A@TEST.INVALID", Email = "user-a@test.invalid",
                    NormalizedEmail = "USER-A@TEST.INVALID", SecurityStamp = Guid.NewGuid().ToString("N")
                });
                database.Users.Add(new ApplicationUser
                {
                    Id = "user-b", UserName = "user-b@test.invalid",
                    NormalizedUserName = "USER-B@TEST.INVALID", Email = "user-b@test.invalid",
                    NormalizedEmail = "USER-B@TEST.INVALID", SecurityStamp = Guid.NewGuid().ToString("N")
                });
                await database.SaveChangesAsync();
            }
            return new(root, provider, images, containers, signal, secrets, healthDelay,
                apiRoot, hostRoot, collision, secretReference);
        }

        public async Task<ProvisioningExecutionResult> ScheduleAsync(string id)
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IGameServerProvisioningService>()
                .ScheduleProvisioningAsync(new(id, "palworld", $"Server {id}"), CancellationToken.None);
        }

        public async Task ExecuteWorkerAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProvisioningOperationExecutor>()
                .ExecuteEligibleAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ApplicationBridge(IServiceProvider provider, IHttpContextAccessor accessor)
        : IManagedGameServerApplicationService
    {
        public async Task<CreateManagedGameServerResult> CreateAsync(CreateManagedGameServerRequest? request,
            string? key, CancellationToken token)
        {
            await using var scope = provider.CreateAsyncScope();
            var service = new ManagedGameServerApplicationService(
                scope.ServiceProvider.GetRequiredService<IGameServerProvisioningService>(),
                new HttpCurrentUser(accessor));
            return await service.CreateAsync(request, key, token);
        }
    }

    private sealed class FixedCurrentUser(string userId) : GamesHud.Api.Authentication.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
    }

    private sealed class QueryBridge(IServiceProvider provider) : IManagedGameServerQueryService
    {
        public async Task<ManagedGameServerResponse?> GetAsync(string ownerId, string id, CancellationToken token)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IManagedGameServerQueryService>().GetAsync(ownerId, id, token);
        }
        public async Task<ManagedProvisioningResponse?> GetProvisioningAsync(string ownerId, string id, CancellationToken token)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IManagedGameServerQueryService>()
                .GetProvisioningAsync(ownerId, id, token);
        }
    }

    private sealed class TrackingSignal : IProvisioningExecutionSignal
    {
        public bool Signalled { get; private set; }
        public void Signal() => Signalled = true;
        public Task WaitAsync(TimeSpan maximumDelay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InitialCodec(PalworldProvisioningConfigurationCodec inner,
        GameSecretReference? server, GameSecretReference? admin) : IGameProvisioningConfigurationCodec
    {
        public GamesHud.Api.GameServers.Domain.GameId GameId => inner.GameId;
        public string ConfigurationKind => inner.ConfigurationKind;
        public int SchemaVersion => inner.SchemaVersion;
        public ValidatedGameProvisioningConfiguration CreateInitial(string serverName,
            GameSecretReference? serverPassword = null, GameSecretReference? adminPassword = null) =>
            inner.CreateInitial(serverName, server, admin);
        public ValidatedGameProvisioningConfiguration Deserialize(string gameId, string kind, int schemaVersion, string payload) =>
            inner.Deserialize(gameId, kind, schemaVersion, payload);
    }

    private sealed class FakeHostCapabilities(string daemonArchitecture) : IHostCapabilityService
    {
        public Task<HostCapabilitySnapshot> GetCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult(new HostCapabilitySnapshot(
            new("linux", "Linux", "x64"), new(8, "x64"),
            new(HostCapabilityStatuses.Available, 32UL * 1024 * 1024 * 1024, 24UL * 1024 * 1024 * 1024),
            new(HostCapabilityStatuses.Available, "/", 500UL * 1024 * 1024 * 1024, 400UL * 1024 * 1024 * 1024),
            new(HostCapabilityStatuses.Available, 1, true, true, true),
            [new("docker", "Docker", HostCapabilityStatuses.Available, true, true, "test", "linux", [], daemonArchitecture)],
            new(HostReadinessStatuses.Ready, "Ready"), []));
    }

    private sealed class FakePortAvailability(bool collision) : IPortAvailabilityService
    {
        public Task<PortAvailability> CheckAvailabilityAsync(NetworkPort port, CancellationToken cancellationToken)
        {
            var available = !(collision && port.Number == 8211 && port.Protocol == PortProtocols.Udp);
            return Task.FromResult(new PortAvailability(port,
                available ? PortAvailabilityStatuses.Available : PortAvailabilityStatuses.InUse,
                available, [], available ? "Available" : "Occupied"));
        }
    }

    private sealed class FakeStorageInfo : IHostStorageInfoProvider
    {
        public HostStorageDriveInfo GetDriveInfo(string path) =>
            new(path, 500UL * 1024 * 1024 * 1024, 400UL * 1024 * 1024 * 1024);
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly Dictionary<SecretId, SecretValue> _values = [];
        public int ResolveCount { get; private set; }
        public Task<GameSecretReference> StoreAsync(SecretPurpose purpose, SecretValue value, CancellationToken cancellationToken = default)
        {
            var reference = new GameSecretReference(SecretId.New());
            _values.Add(reference.Id, value);
            return Task.FromResult(reference);
        }
        public Task<SecretValue> GetAsync(GameSecretReference reference, CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return Task.FromResult(_values[reference.Id]);
        }
        public Task ReplaceAsync(GameSecretReference reference, SecretValue value, CancellationToken cancellationToken = default)
        { _values[reference.Id] = value; return Task.CompletedTask; }
        public Task DeleteAsync(GameSecretReference reference, CancellationToken cancellationToken = default)
        { _values.Remove(reference.Id); return Task.CompletedTask; }
    }

    private sealed class FakeDockerImages(bool initiallyAbsent) : IDockerImageAcquisitionClient
    {
        private bool _present = !initiallyAbsent;
        private bool _pulled;
        public bool FailNextInspect { get; set; }
        public bool FailFinalInspectAfterPull { get; set; }
        public string PullResult { get; set; } = DockerImagePullStatuses.Dispatched;
        public int InspectCount { get; private set; }
        public int PullCount { get; private set; }
        public List<string> InspectedReferences { get; } = [];
        public List<string> PulledReferences { get; } = [];
        public List<string> PulledPlatforms { get; } = [];
        public Task<DockerImageInspectionSnapshot?> InspectAsync(string reference, CancellationToken cancellationToken)
        {
            InspectCount++;
            InspectedReferences.Add(reference);
            if (FailNextInspect)
            {
                FailNextInspect = false;
                throw new HttpRequestException("simulated provider interruption");
            }
            if (FailFinalInspectAfterPull && _pulled)
            {
                FailFinalInspectAfterPull = false;
                throw new OperationCanceledException();
            }
            return Task.FromResult<DockerImageInspectionSnapshot?>(_present
                ? new(LocalImageId, [ApprovedReference], "linux", "amd64", null) : null);
        }
        public Task<DockerImagePullResult> PullAsync(string reference, string platform, CancellationToken cancellationToken)
        {
            PullCount++;
            PulledReferences.Add(reference);
            PulledPlatforms.Add(platform);
            _pulled = true;
            _present = PullResult == DockerImagePullStatuses.Dispatched;
            return Task.FromResult(new DockerImagePullResult(PullResult));
        }
    }

    private sealed class FakeDockerRuntime : IDockerManagedRuntimeClient
    {
        private string _state = ManagedRuntimeStates.Absent;
        private const string ContainerId = "container-e2e";
        public int CreateCount { get; private set; }
        public int StartCount { get; private set; }
        public bool FailCreateAfterDispatch { get; set; }
        public bool FailStartAfterDispatch { get; set; }
        public bool AmbiguousCreateAfterDispatch { get; set; }
        private bool _ambiguous;
        public int StartingHealthInspections { get; set; }
        public List<CreateContainerParameters> Created { get; } = [];
        public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken) => Task.FromResult(image == LocalImageId);
        public Task<IReadOnlyCollection<ContainerListResponse>> ListAsync(CancellationToken cancellationToken)
        {
            if (_ambiguous) throw new HttpRequestException("simulated ambiguous Docker inspection");
            if (_state == ManagedRuntimeStates.Absent) return Task.FromResult<IReadOnlyCollection<ContainerListResponse>>([]);
            var expected = Created.Single();
            return Task.FromResult<IReadOnlyCollection<ContainerListResponse>>([new ContainerListResponse
            {
                ID = ContainerId, Names = ["/" + expected.Name], Image = expected.Image,
                Labels = expected.Labels
            }]);
        }
        public Task<ContainerInspectResponse> InspectAsync(string id, CancellationToken cancellationToken)
        {
            var expected = Created.Single();
            var health = "healthy";
            if (_state == ManagedRuntimeStates.Running && StartingHealthInspections > 0)
            {
                StartingHealthInspections--;
                health = "starting";
            }
            return Task.FromResult(new ContainerInspectResponse
            {
                ID = ContainerId,
                Image = expected.Image,
                Config = new Config { Image = expected.Image, Env = expected.Env },
                HostConfig = expected.HostConfig,
                State = new ContainerState
                {
                    Status = _state == ManagedRuntimeStates.Running ? "running" : "created",
                    Running = _state == ManagedRuntimeStates.Running,
                    Health = new Health { Status = _state == ManagedRuntimeStates.Running ? health : "starting" }
                }
            });
        }
        public Task<CreateContainerResponse> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
        {
            CreateCount++;
            Created.Add(parameters);
            _state = ManagedRuntimeStates.Created;
            if (AmbiguousCreateAfterDispatch)
            {
                _ambiguous = true;
                throw new HttpRequestException("simulated lost create response");
            }
            if (FailCreateAfterDispatch)
            {
                FailCreateAfterDispatch = false;
                throw new HttpRequestException("simulated lost create response");
            }
            return Task.FromResult(new CreateContainerResponse { ID = ContainerId });
        }
        public Task<bool> StartAsync(string id, CancellationToken cancellationToken)
        {
            StartCount++;
            _state = ManagedRuntimeStates.Running;
            if (FailStartAfterDispatch)
            {
                FailStartAfterDispatch = false;
                throw new HttpRequestException("simulated lost start response");
            }
            return Task.FromResult(true);
        }
    }

    private sealed class FakeHealthDelay : IRuntimeHealthDelay
    {
        public int WaitCount { get; private set; }
        public bool InterruptNextWait { get; set; }
        public Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            WaitCount++;
            if (InterruptNextWait)
            {
                InterruptNextWait = false;
                throw new OperationCanceledException();
            }
            return Task.CompletedTask;
        }
    }
}
