using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Configuration;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Palworld.ManagedConfiguration;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using GamesHud.Api.Secrets.Models;
using GamesHud.Api.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using Xunit.Sdk;
using GameSecretReference = GamesHud.Api.Secrets.Models.SecretReference;

namespace GamesHud.Api.Tests;

public sealed class DockerIntegrationFactAttribute : FactAttribute
{
    public const string OptInVariable = "GAMESHUD_RUN_DOCKER_INTEGRATION_TESTS";
    public const string DiskConfirmationVariable = "GAMESHUD_DOCKER_INTEGRATION_CONFIRMED_FREE_DISK_GB";

    public DockerIntegrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "true", StringComparison.OrdinalIgnoreCase))
            Skip = $"Set {OptInVariable}=true and run only Category=DockerIntegration after human approval.";
    }
}

public sealed class DockerIntegrationValidationTests(ITestOutputHelper output)
{
    private const string ApprovedDigest = "sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9";
    private const string ApprovedReference = "docker.io/thijsvanloef/palworld-server-docker@" + ApprovedDigest;
    private const long Gibibyte = 1024L * 1024 * 1024;
    private const long RequiredDaemonMemoryBytes = 12 * Gibibyte;
    private const ulong RequiredAvailableHostMemoryBytes = 10UL * 1024 * 1024 * 1024;
    private const long RequiredFreeDiskBytes = 30 * Gibibyte;
    private static readonly TimeSpan PalworldHealthTimeout = TimeSpan.FromSeconds(600);

    [DockerIntegrationFact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApprovedPalworldArtifactSurvivesCreateStartHealthAndRestart()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(90));
        using var docker = new DockerClientConfiguration().CreateClient();
        var identity = Guid.NewGuid().ToString("N")[..12];
        var serverId = new GameServerId($"it-palworld-{identity}");
        var containerName = $"gameshud-{serverId}";
        var sandboxParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gameshud-it"));
        var sandbox = Path.GetFullPath(Path.Combine(sandboxParent, identity));
        string? containerId = null;

        try
        {
            output.WriteLine("Layer 1/5: read-only Docker and resource preflight.");
            await PreflightAsync(docker, containerName, sandboxParent, sandbox, cancellation.Token);
            Directory.CreateDirectory(sandbox);

            var hostPorts = AllocateDistinctUdpPorts(2);
            output.WriteLine($"test_namespace=gameshud-it-{identity}");
            output.WriteLine($"approved_digest={ApprovedDigest}");
            output.WriteLine($"sandbox_path={sandbox}");
            output.WriteLine($"allocated_host_ports={hostPorts[0]}/udp,{hostPorts[1]}/udp");

            await using var state = await DurableState.CreateAsync(sandbox, serverId, hostPorts, cancellation.Token);
            await state.ExecuteStepAsync(state.PrepareStorageStep(), cancellation.Token);
            await state.ExecuteStepAsync(state.ConfigureGameStep(), cancellation.Token);
            AssertIni(state, beforeStart: true);

            output.WriteLine("Layer 2/5: approved digest acquisition and final inspection.");
            await state.ExecuteStepAsync(state.AcquireImageStep(), cancellation.Token);
            var verified = await state.Images.LoadVerifiedAsync(state.ImageOwner, cancellation.Token);
            Assert.Equal(ApprovedReference, verified.ApprovedImage.Reference);
            Assert.StartsWith("sha256:", verified.LocalImageId.Value, StringComparison.Ordinal);
            Assert.NotEqual(ApprovedDigest, verified.LocalImageId.Value);
            var imageInspection = await docker.Images.InspectImageAsync(ApprovedReference, cancellation.Token);
            Assert.Contains(ApprovedReference, imageInspection.RepoDigests ?? []);
            Assert.Equal(verified.LocalImageId.Value, imageInspection.ID);
            output.WriteLine($"local_image_id={verified.LocalImageId.Value}");

            var specification = await state.RuntimeSpecifications.BuildAsync(state.Context, cancellation.Token);
            Assert.NotNull(specification);
            Assert.Equal(verified.LocalImageId, specification!.VerifiedImage!.LocalImageId);
            var policy = state.RuntimePolicy.Validate(specification, state.Definition, sandbox);
            Assert.True(policy.Allowed, string.Join(", ", policy.Violations.Select(item => item.Code)));
            var createContext = new RuntimeMutationExecutionContext(policy.Specification!, RuntimeMutationKind.CreateRuntime,
                ProvisioningStepIds.CreateRuntime, 1);
            var expectedCreate = DockerCreateContainerMapper.Map(createContext);
            Assert.Equal(verified.LocalImageId.Value, expectedCreate.Image);
            Assert.Equal(containerName, expectedCreate.Name);
            Assert.Null(expectedCreate.Cmd);
            Assert.Null(expectedCreate.Entrypoint);

            output.WriteLine("Layer 3/5: stopped container creation, bind and port inspection.");
            await state.ExecuteStepAsync(state.CreateRuntimeStep(), cancellation.Token);
            containerId = await FindOwnedContainerIdAsync(docker, containerName, serverId.ToString(), cancellation.Token);
            output.WriteLine($"container_id={containerId}");
            var stopped = await docker.Containers.InspectContainerAsync(containerId, cancellation.Token);
            AssertContainerContract(stopped, state, verified.LocalImageId.Value, hostPorts, running: false);

            output.WriteLine("Layer 4/5: start, Docker HEALTHCHECK and semantic config preservation.");
            await state.ExecuteStepAsync(state.StartRuntimeStep(), cancellation.Token);
            var firstHealth = await WaitForHealthyAsync(docker, containerId, cancellation.Token);
            output.WriteLine($"first_health_duration={firstHealth}");
            await state.ExecuteStepAsync(state.VerifyHealthStep(), cancellation.Token);
            AssertIni(state, beforeStart: false);

            output.WriteLine("Layer 5/5: same-container restart persistence.");
            var beforeRestart = await docker.Containers.InspectContainerAsync(containerId, cancellation.Token);
            var stoppedCleanly = await docker.Containers.StopContainerAsync(containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 30 }, cancellation.Token);
            Assert.True(stoppedCleanly);
            var restartContext = new RuntimeMutationExecutionContext(policy.Specification!, RuntimeMutationKind.StartRuntime,
                ProvisioningStepIds.StartRuntime, 1);
            var restarted = await state.RuntimeAdapter.StartAsync(restartContext, cancellation.Token);
            Assert.Equal(RuntimeMutationOutcomeStatuses.Success, restarted.Status);
            var secondHealth = await WaitForHealthyAsync(docker, containerId, cancellation.Token);
            output.WriteLine($"restart_health_duration={secondHealth}");
            var afterRestart = await docker.Containers.InspectContainerAsync(containerId, cancellation.Token);
            Assert.Equal(beforeRestart.ID, afterRestart.ID);
            AssertContainerContract(afterRestart, state, verified.LocalImageId.Value, hostPorts, running: true);
            AssertIni(state, beforeStart: false);

            await state.ExecuteStepAsync(new NoHostMutationProvisioningStep(ProvisioningStepIds.Complete), cancellation.Token);
            var final = await state.Operations.GetAsync(state.Reservation.ProvisioningOperationId, cancellation.Token);
            await state.Operations.FinalizeAsync(final!.OperationId, final.Version, cancellation.Token);
        }
        finally
        {
            containerId ??= await TryFindOwnedContainerIdAsync(docker, containerName, serverId.ToString());
            await CleanupContainerAsync(docker, containerId, containerName, serverId.ToString());
            CleanupSandbox(sandboxParent, sandbox);
        }
    }

    private async Task PreflightAsync(IDockerClient docker, string containerName, string sandboxParent,
        string sandbox, CancellationToken cancellationToken)
    {
        Assert.Equal("true", Environment.GetEnvironmentVariable(DockerIntegrationFactAttribute.OptInVariable), ignoreCase: true);
        Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")),
            "DOCKER_HOST must be unset so validation cannot target a remote daemon.");
        Assert.True(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")),
            "DOCKER_CONTEXT must be unset so validation cannot target a remote/shared context.");
        Assert.True(IsContained(sandboxParent, sandbox));
        Assert.Contains("gameshud-it", sandboxParent, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("gameshud-it-palworld-", containerName, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/containers", sandbox.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(sandbox));

        var version = await docker.System.GetVersionAsync(cancellationToken);
        Assert.Equal("linux", version.Os, ignoreCase: true);
        Assert.Equal("amd64", NormalizeArchitecture(version.Arch));
        var info = await docker.System.GetSystemInfoAsync(cancellationToken);
        Assert.True(info.MemTotal >= RequiredDaemonMemoryBytes,
            $"Docker must expose at least {RequiredDaemonMemoryBytes / Gibibyte} GiB; observed {info.MemTotal / Gibibyte} GiB.");
        var availableMemory = GetAvailablePhysicalMemoryBytes();
        Assert.True(availableMemory >= RequiredAvailableHostMemoryBytes,
            $"Host must have at least {RequiredAvailableHostMemoryBytes / (ulong)Gibibyte} GiB available before start.");
        var tempDrive = new DriveInfo(Path.GetPathRoot(sandbox)!);
        Assert.True(tempDrive.AvailableFreeSpace >= RequiredFreeDiskBytes,
            $"Sandbox drive must have at least {RequiredFreeDiskBytes / Gibibyte} GiB free.");
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable(
                DockerIntegrationFactAttribute.DiskConfirmationVariable), out var confirmedDiskGiB)
            && confirmedDiskGiB >= RequiredFreeDiskBytes / Gibibyte,
            $"Set {DockerIntegrationFactAttribute.DiskConfirmationVariable} to a verified Docker-daemon free-space value of at least 30 GiB.");

        var existing = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
        Assert.DoesNotContain(existing, item => item.Names?.Contains("/" + containerName, StringComparer.Ordinal) == true);
        output.WriteLine($"daemon={version.Os}/{NormalizeArchitecture(version.Arch)} memory_gib={info.MemTotal / Gibibyte}");
        output.WriteLine($"host_available_memory_gib={availableMemory / (ulong)Gibibyte} sandbox_free_disk_gib={tempDrive.AvailableFreeSpace / Gibibyte}");
    }

    private static int[] AllocateDistinctUdpPorts(int count)
    {
        var ports = new HashSet<int>();
        while (ports.Count < count)
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            ports.Add(((IPEndPoint)socket.Client.LocalEndPoint!).Port);
        }
        return ports.ToArray();
    }

    private static void AssertIni(DurableState state, bool beforeStart)
    {
        var path = state.IniPath;
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.True(state.Serializer.TryParse(content, out var parsed));
        Assert.Equal(state.ExpectedConfiguration, parsed);
        if (beforeStart) Assert.Equal(["DISABLE_GENERATE_SETTINGS=true"], state.ExpectedEnvironment);
        Assert.True(IsContained(state.Sandbox, path));
    }

    private static void AssertContainerContract(ContainerInspectResponse actual, DurableState state,
        string localImageId, IReadOnlyList<int> hostPorts, bool running)
    {
        Assert.Equal(localImageId, actual.Image);
        Assert.Equal(localImageId, actual.Config.Image);
        Assert.Equal("true", actual.Config.Labels[DockerManagedRuntimeLabels.Managed]);
        Assert.Equal(state.ServerId.ToString(), actual.Config.Labels[DockerManagedRuntimeLabels.GameServerId]);
        Assert.Equal("palworld", actual.Config.Labels[DockerManagedRuntimeLabels.GameId]);
        Assert.Equal(["DISABLE_GENERATE_SETTINGS=true"], actual.Config.Env);
        Assert.False(actual.HostConfig.Privileged);
        Assert.Equal("default", actual.HostConfig.NetworkMode);
        Assert.Equal(RestartPolicyKind.UnlessStopped, actual.HostConfig.RestartPolicy.Name);
        Assert.Contains($"{state.DataPath}:/palworld:rw", actual.HostConfig.Binds);
        Assert.Contains("8211/udp", actual.Config.ExposedPorts.Keys);
        Assert.Contains("27015/udp", actual.Config.ExposedPorts.Keys);
        Assert.Contains("8212/tcp", actual.Config.ExposedPorts.Keys);
        Assert.Equal(hostPorts[0].ToString(), Assert.Single(actual.HostConfig.PortBindings["8211/udp"]).HostPort);
        Assert.Equal(hostPorts[1].ToString(), Assert.Single(actual.HostConfig.PortBindings["27015/udp"]).HostPort);
        Assert.DoesNotContain("8212/tcp", actual.HostConfig.PortBindings.Keys);
        Assert.Equal(running, actual.State.Running);
    }

    private async Task<TimeSpan> WaitForHealthyAsync(IDockerClient docker, string containerId,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var deadline = started + PalworldHealthTimeout;
        string? prior = null;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            var inspected = await docker.Containers.InspectContainerAsync(containerId, cancellationToken);
            var health = inspected.State.Health?.Status;
            var observed = health is null ? inspected.State.Status : $"{inspected.State.Status}/{health}";
            if (observed != prior)
            {
                output.WriteLine($"health_transition={observed}");
                prior = observed;
            }
            if (!inspected.State.Running) throw new XunitException($"Container stopped during readiness: {observed}.");
            if (health is null or "healthy") return DateTimeOffset.UtcNow - started;
            if (health == "unhealthy") throw new XunitException("Docker HEALTHCHECK reported unhealthy.");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        throw new XunitException("Palworld did not become healthy within 600 seconds.");
    }

    private static async Task<string> FindOwnedContainerIdAsync(IDockerClient docker, string name,
        string serverId, CancellationToken cancellationToken) =>
        await TryFindOwnedContainerIdAsync(docker, name, serverId, cancellationToken)
        ?? throw new XunitException("The exclusively named integration container was not found.");

    private static async Task<string?> TryFindOwnedContainerIdAsync(IDockerClient docker, string name,
        string serverId, CancellationToken cancellationToken = default)
    {
        try
        {
            var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken);
            var candidates = containers.Where(item => item.Names?.Contains("/" + name, StringComparer.Ordinal) == true
                && item.Labels is not null
                && item.Labels.TryGetValue(DockerManagedRuntimeLabels.Managed, out var managed) && managed == "true"
                && item.Labels.TryGetValue(DockerManagedRuntimeLabels.GameServerId, out var owner) && owner == serverId).ToArray();
            return candidates.Length == 1 ? candidates[0].ID : null;
        }
        catch { return null; }
    }

    private async Task CleanupContainerAsync(IDockerClient docker, string? containerId, string name, string serverId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            output.WriteLine("cleanup_container=not_created");
            return;
        }
        try
        {
            var actual = await docker.Containers.InspectContainerAsync(containerId);
            var owned = actual.Name == "/" + name
                && actual.Config?.Labels?.TryGetValue(DockerManagedRuntimeLabels.Managed, out var managed) == true && managed == "true"
                && actual.Config.Labels.TryGetValue(DockerManagedRuntimeLabels.GameServerId, out var owner) && owner == serverId;
            if (!owned) throw new InvalidOperationException("Cleanup ownership could not be proven.");
            if (actual.State?.Running == true)
                await docker.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 30 });
            await docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = false, RemoveVolumes = false });
            output.WriteLine($"cleanup_container=removed id={containerId}");
        }
        catch (Exception exception)
        {
            output.WriteLine($"cleanup_container=failed id={containerId} error={exception.GetType().Name}");
            output.WriteLine($"manual_cleanup=docker inspect {containerId}; docker rm -f {containerId}");
        }
    }

    private void CleanupSandbox(string parent, string sandbox)
    {
        try
        {
            if (Directory.Exists(sandbox) && IsContained(parent, sandbox)) Directory.Delete(sandbox, recursive: true);
            output.WriteLine("cleanup_sandbox=removed");
        }
        catch (Exception exception)
        {
            output.WriteLine($"cleanup_sandbox=failed path={sandbox} error={exception.GetType().Name}");
            output.WriteLine($"manual_cleanup=Remove-Item -LiteralPath '{sandbox}' -Recurse -Force");
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string NormalizeArchitecture(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "amd64" or "x64" or "x86_64" => "amd64",
        "arm64" or "aarch64" => "arm64",
        _ => value?.Trim().ToLowerInvariant() ?? string.Empty
    };

    private static ulong GetAvailablePhysicalMemoryBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status)) return status.AvailablePhysical;
        }
        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            var line = File.ReadLines("/proc/meminfo").FirstOrDefault(item => item.StartsWith("MemAvailable:", StringComparison.Ordinal));
            var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts?.Length >= 2 && ulong.TryParse(parts[1], out var kibibytes)) return kibibytes * 1024;
        }
        return checked((ulong)Math.Max(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>());
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed class DurableState : IAsyncDisposable
    {
        private readonly GamesHudDbContext _db;
        private readonly IManagedStoragePathBuilder _paths;
        private readonly IManagedServerStore _servers;
        private readonly IGameDefinitionRegistry _definitions;
        private readonly IGameProvisioningConfigurationStore _configurations;
        private readonly ISecretStore _secrets = new NoSecretStore();
        private readonly IRuntimeImageAcquisitionAdapter _imageAdapter;
        private readonly IRuntimeMutationExecutor _runtimeExecutor;

        private DurableState(string sandbox, GameServerId serverId, PalworldGameDefinition definition,
            GamesHudDbContext db, IManagedStoragePathBuilder paths, IManagedServerStore servers,
            IGameDefinitionRegistry definitions, IGameProvisioningConfigurationStore configurations,
            IProvisioningOperationStore operations, IRuntimeImageIntentStore images,
            ManagedServerReservationResult reservation, ProvisioningContext context,
            PalworldProvisioningConfigurationCodec codec, PalworldManagedConfigurationSerializer serializer,
            RuntimeSpecificationBuilder runtimeSpecifications, RuntimeMutationPolicy runtimePolicy,
            DockerGameRuntimeAdapter runtimeAdapter, IRuntimeImageAcquisitionAdapter imageAdapter,
            IRuntimeMutationExecutor runtimeExecutor)
        {
            Sandbox = sandbox;
            ServerId = serverId;
            Definition = definition;
            _db = db;
            _paths = paths;
            _servers = servers;
            _definitions = definitions;
            _configurations = configurations;
            Operations = operations;
            Images = images;
            Reservation = reservation;
            Context = context;
            Codec = codec;
            Serializer = serializer;
            RuntimeSpecifications = runtimeSpecifications;
            RuntimePolicy = runtimePolicy;
            RuntimeAdapter = runtimeAdapter;
            _imageAdapter = imageAdapter;
            _runtimeExecutor = runtimeExecutor;
        }

        public string Sandbox { get; }
        public GameServerId ServerId { get; }
        public PalworldGameDefinition Definition { get; }
        public IProvisioningOperationStore Operations { get; }
        public IRuntimeImageIntentStore Images { get; }
        public ManagedServerReservationResult Reservation { get; }
        public ProvisioningContext Context { get; }
        public PalworldProvisioningConfigurationCodec Codec { get; }
        public PalworldManagedConfigurationSerializer Serializer { get; }
        public RuntimeSpecificationBuilder RuntimeSpecifications { get; }
        public RuntimeMutationPolicy RuntimePolicy { get; }
        public DockerGameRuntimeAdapter RuntimeAdapter { get; }
        public string DataPath => Context.ValidatedPlan.Storage.Single().ApiPath!;
        public string IniPath => Path.Combine(DataPath, PalworldManagedConfigurationTargetBuilder.RelativeConfigurationPath);
        public PalworldManagedConfiguration ExpectedConfiguration => new($"GamesHud IT {ServerId}", string.Empty, 32, "None", string.Empty, string.Empty);
        public string[] ExpectedEnvironment => ["DISABLE_GENERATE_SETTINGS=true"];
        public RuntimeImageOwner ImageOwner => new(Reservation.ProvisioningOperationId, ServerId.ToString(), "palworld", "docker");

        public static async Task<DurableState> CreateAsync(string sandbox, GameServerId serverId,
            IReadOnlyList<int> hostPorts, CancellationToken cancellationToken)
        {
            var options = new DbContextOptionsBuilder<GamesHudDbContext>()
                .UseSqlite($"Data Source={Path.Combine(sandbox, "integration.db")};Pooling=False").Options;
            var db = new GamesHudDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            var transactions = new EfCorePersistenceTransactionBoundary(db);
            var paths = new ManagedStoragePathBuilder(Options.Create(new StorageOptions
            {
                DataRoot = sandbox,
                ManagedApiRoot = sandbox,
                ManagedHostRoot = sandbox
            }));
            var definition = new PalworldGameDefinition();
            var definitions = new GameDefinitionRegistry([definition]);
            var servers = new ManagedServerStore(db, transactions, paths);
            var operations = new ProvisioningOperationStore(db, transactions, new ProvisioningStateMachine());
            var images = new RuntimeImageIntentStore(db, transactions);
            var configurations = new GameProvisioningConfigurationStore(db);
            var codec = new PalworldProvisioningConfigurationCodec();
            var configuration = codec.CreateInitial($"GamesHud IT {serverId}");
            var layout = paths.CreateLayout(serverId);
            var dataPath = Path.Combine(layout.ServerRoot, "data");
            var plan = new ManagedServerProvisioningPlan(serverId.ToString(), "palworld", $"GamesHud IT {serverId}", "docker",
                [
                    new("game", "udp", 8211, hostPorts[0], true, PortExposures.Public),
                    new("query", "udp", 27015, hostPorts[1], true, PortExposures.Public),
                    new("rest-api", "tcp", 8212, null, false, PortExposures.Internal)
                ],
                [new("data", $"servers/{serverId}/data", dataPath, dataPath)],
                configuration,
                ProvisioningPipelines.ImageAcquisitionVersion,
                ProvisioningPipelines.ImageAcquisition.Steps.Select(step => new ProvisioningStepPlan(step.Id,
                    step.Sequence, step.RetryClassification, step.SideEffectClassification, step.MaxAttempts,
                    step.Sequence <= 3)).ToArray(),
                definition.RuntimeImages.Single());
            var reservation = await servers.ReserveProvisioningPlanAsync(plan, cancellationToken);
            var validated = new ValidatedProvisioningPlan(serverId, new GameId("palworld"), plan.DisplayName, "docker",
                GameCompatibilityStatuses.Compatible, [],
                [
                    new("game", "udp", 8211, hostPorts[0], true, PortExposures.Public),
                    new("query", "udp", 27015, hostPorts[1], true, PortExposures.Public),
                    new("rest-api", "tcp", 8212, null, false, PortExposures.Internal)
                ],
                [new("data", $"servers/{serverId}/data", dataPath, dataPath)], [],
                ProvisioningPipelines.ImageAcquisition.Steps.Select(step => step.Id).ToArray(), configuration);
            var context = new ProvisioningContext(reservation.ProvisioningOperationId, definition, validated, reservation,
                userRequestedCancellation: false);

            var dockerOptions = Options.Create(new DockerOptions());
            var imageOptions = Options.Create(new RuntimeImageAcquisitionOptions { TimeoutSeconds = 3600, InspectTimeoutSeconds = 60 });
            var imageAdapter = new DockerRuntimeImageAcquisitionAdapter(new DockerImageAcquisitionClient(dockerOptions), imageOptions);
            var runtimeClient = new DockerManagedRuntimeClient(dockerOptions);
            var runtimeAdapter = new DockerGameRuntimeAdapter(runtimeClient, new ManagedRuntimeStorageValidator());
            var runtimeExecutor = new RuntimeMutationExecutor(runtimeAdapter, NullLogger<RuntimeMutationExecutor>.Instance);
            var runtimeSpecifications = new RuntimeSpecificationBuilder(servers, paths, definitions, operations, images);
            return new(sandbox, serverId, definition, db, paths, servers, definitions, configurations, operations, images,
                reservation, context, codec, new PalworldManagedConfigurationSerializer(), runtimeSpecifications,
                new RuntimeMutationPolicy(), runtimeAdapter, imageAdapter, runtimeExecutor);
        }

        public IProvisioningStep PrepareStorageStep() => new PrepareStorageProvisioningStep(
            new ManagedStorageTargetBuilder(_servers, _paths, _definitions),
            new ManagedStorageProvider(new SystemManagedDirectoryOperations()),
            NullLogger<PrepareStorageProvisioningStep>.Instance);

        public IProvisioningStep ConfigureGameStep()
        {
            var targets = new PalworldManagedConfigurationTargetBuilder(
                new ManagedStorageTargetBuilder(_servers, _paths, _definitions), _definitions);
            var intent = new PalworldManagedConfigurationIntentReader(_configurations, Codec, _secrets, Serializer);
            var files = new PalworldManagedConfigurationFileStore(new SystemPalworldManagedConfigurationFileSystem(), Serializer);
            return new ConfigurePalworldGameProvisioningStep(targets, intent, files,
                NullLogger<ConfigurePalworldGameProvisioningStep>.Instance);
        }

        public IProvisioningStep AcquireImageStep() => new AcquireImageProvisioningStep(Images, _imageAdapter,
            Options.Create(new RuntimeImageAcquisitionOptions { TimeoutSeconds = 3600, InspectTimeoutSeconds = 60 }));

        public IProvisioningStep CreateRuntimeStep() => new CreateRuntimeProvisioningStep(RuntimeSpecifications,
            RuntimePolicy, _runtimeExecutor, _paths, NullLogger<CreateRuntimeProvisioningStep>.Instance);

        public IProvisioningStep StartRuntimeStep() => new StartRuntimeProvisioningStep(RuntimeSpecifications,
            RuntimePolicy, _paths, _runtimeExecutor);

        public IProvisioningStep VerifyHealthStep() => new VerifyRuntimeHealthProvisioningStep(RuntimeSpecifications,
            RuntimePolicy, _paths, RuntimeAdapter, new RuntimeHealthDelay(), Options.Create(new RuntimeHealthOptions
            {
                TimeoutSeconds = 60,
                PalworldTimeoutSeconds = 600,
                PollIntervalSeconds = 5
            }));

        public async Task ExecuteStepAsync(IProvisioningStep step, CancellationToken cancellationToken)
        {
            var before = await Operations.GetAsync(Reservation.ProvisioningOperationId, cancellationToken)
                ?? throw new XunitException("Provisioning operation disappeared.");
            var running = await Operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(before.OperationId,
                before.Version, ProvisioningOperationStatuses.Running, step.Id, step.Id,
                ProvisioningStepStatuses.Running), cancellationToken);
            var result = await step.ExecuteAsync(Context, cancellationToken);
            if (result.Status != ProvisioningStepResultStatuses.Succeeded)
                throw new XunitException($"Step {step.Id} failed safely with {result.ErrorCode}: {result.SafeMessage}");
            await Operations.ApplyCheckpointAsync(new ProvisioningCheckpoint(running.OperationId, running.Version,
                ProvisioningOperationStatuses.Running, step.Id, step.Id, ProvisioningStepStatuses.Succeeded),
                CancellationToken.None);
        }

        public async ValueTask DisposeAsync() => await _db.DisposeAsync();
    }

    private sealed class NoSecretStore : ISecretStore
    {
        public Task<GameSecretReference> StoreAsync(SecretPurpose purpose, SecretValue value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Docker integration validation must not store secrets.");
        public Task<SecretValue> GetAsync(GameSecretReference reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Docker integration validation must not resolve secrets.");
        public Task ReplaceAsync(GameSecretReference reference, SecretValue value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Docker integration validation must not replace secrets.");
        public Task DeleteAsync(GameSecretReference reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Docker integration validation must not delete secrets.");
    }
}
