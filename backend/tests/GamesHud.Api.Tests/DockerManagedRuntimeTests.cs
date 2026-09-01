using Docker.DotNet.Models;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;

namespace GamesHud.Api.Tests;

public sealed class DockerManagedRuntimeTests
{
    [Fact]
    public void MappingIsBackendControlledAndPublishesOnlyPublicPorts()
    {
        var request = DockerCreateContainerMapper.Map(Context());

        Assert.Equal("gameshud-server-1", request.Name);
        Assert.Equal("trusted/game:1", request.Image);
        Assert.Equal("true", request.Labels[DockerManagedRuntimeLabels.Managed]);
        Assert.Equal("server-1", request.Labels[DockerManagedRuntimeLabels.GameServerId]);
        Assert.Equal("game-1", request.Labels[DockerManagedRuntimeLabels.GameId]);
        Assert.Equal("server-1", request.Labels[DockerManagedRuntimeLabels.Identity]);
        Assert.Null(request.Cmd);
        Assert.Null(request.Entrypoint);
        Assert.Null(request.Env);
        Assert.False(request.HostConfig.Privileged);
        Assert.Equal("default", request.HostConfig.NetworkMode);
        Assert.Equal(2_000_000_000, request.HostConfig.NanoCPUs);
        Assert.Equal(1024, request.HostConfig.Memory);
        Assert.Equal(RestartPolicyKind.UnlessStopped, request.HostConfig.RestartPolicy.Name);
        Assert.Single(request.HostConfig.Binds);
        Assert.Equal("C:\\managed\\server-1:/game:rw", request.HostConfig.Binds[0]);
        Assert.True(request.ExposedPorts.ContainsKey("7000/udp"));
        Assert.True(request.ExposedPorts.ContainsKey("7001/tcp"));
        Assert.True(request.HostConfig.PortBindings.ContainsKey("7000/udp"));
        Assert.False(request.HostConfig.PortBindings.ContainsKey("7001/tcp"));
    }

    [Fact]
    public async Task MissingImageIsKnownFailureAndDoesNotCreate()
    {
        var client = new FakeDockerClient { ImageExists = false };
        var outcome = await Adapter(client).CreateAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.KnownFailure, outcome.Status);
        Assert.Equal(DockerRuntimeErrorCodes.ImageUnavailable, outcome.SafeCode);
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task MatchingExistingStoppedContainerIsIdempotent()
    {
        var context = Context();
        var expected = DockerCreateContainerMapper.Map(context);
        var client = new FakeDockerClient
        {
            Containers = [Summary(expected)],
            Inspection = Inspection(expected)
        };
        var outcome = await Adapter(client).CreateAsync(context, CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Success, outcome.Status);
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task NameCollisionWithoutOwnershipFailsClosed()
    {
        var expected = DockerCreateContainerMapper.Map(Context());
        var collision = Summary(expected);
        collision.Labels = new Dictionary<string, string>();
        var client = new FakeDockerClient { Containers = [collision] };
        var outcome = await Adapter(client).CreateAsync(Context(), CancellationToken.None);
        Assert.Equal(DockerRuntimeErrorCodes.IdentityConflict, outcome.SafeCode);
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task FailureAfterCreateDispatchIsUnknownAndNeverRetriedInternally()
    {
        var client = new FakeDockerClient { CreateException = new IOException("connection lost") };
        var outcome = await Adapter(client).CreateAsync(Context(), CancellationToken.None);
        Assert.Equal(RuntimeMutationOutcomeStatuses.Unknown, outcome.Status);
        Assert.Equal(1, client.CreateCalls);
    }

    [Fact]
    public async Task ReconciliationRejectsRunningOrDriftedContainer()
    {
        var context = Context();
        var expected = DockerCreateContainerMapper.Map(context);
        var inspection = Inspection(expected);
        inspection.State.Running = true;
        var client = new FakeDockerClient { Containers = [Summary(expected)], Inspection = inspection };
        var result = await Adapter(client).ReconcileAsync(context, CancellationToken.None);
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous, result.Outcome);
    }

    [Fact]
    public void MultipleIdentityMatchesAreAmbiguous()
    {
        var expected = DockerCreateContainerMapper.Map(Context());
        var first = Summary(expected);
        var second = Summary(expected); second.ID = "container-2"; second.Names = ["/other"];
        Assert.Equal(ProvisioningReconciliationOutcomes.Ambiguous,
            DockerGameRuntimeAdapter.Classify([first, second], expected));
    }

    private static DockerGameRuntimeAdapter Adapter(FakeDockerClient client) => new(client, new SafeStorage());

    private static RuntimeMutationExecutionContext Context()
    {
        var specification = new RuntimeMutationSpecification(
            new GameServerId("server-1"), new GameId("game-1"), "operation-1", "docker",
            new TrustedRuntimeImage("docker", "trusted/game", "1", "test"),
            [new("port-public", "game", PortProtocols.Udp, 7000, PortExposures.Public),
             new("port-internal", "admin", PortProtocols.Tcp, 7001, PortExposures.Internal)],
            [new("storage-1", "data", "C:\\managed\\server-1", "/game", false)], [],
            new(2, 1024), RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged);
        return new(new ValidatedRuntimeMutationSpecification(specification), RuntimeMutationKind.CreateRuntime,
            ProvisioningStepIds.CreateRuntime, 1);
    }

    private static ContainerListResponse Summary(CreateContainerParameters expected) => new()
    {
        ID = "container-1",
        Names = ["/" + expected.Name],
        Image = expected.Image,
        State = "created",
        Labels = new Dictionary<string, string>(expected.Labels)
    };

    private static ContainerInspectResponse Inspection(CreateContainerParameters expected) => new()
    {
        ID = "container-1",
        Config = new Config { Image = expected.Image },
        State = new ContainerState { Running = false },
        HostConfig = expected.HostConfig
    };

    private sealed class SafeStorage : IManagedRuntimeStorageValidator
    {
        public bool IsPreparedAndSafe(IReadOnlyCollection<RuntimeStorageMount> mounts) => true;
    }

    private sealed class FakeDockerClient : IDockerManagedRuntimeClient
    {
        public bool ImageExists { get; init; } = true;
        public IReadOnlyCollection<ContainerListResponse> Containers { get; init; } = [];
        public ContainerInspectResponse Inspection { get; init; } = new();
        public Exception? CreateException { get; init; }
        public int CreateCalls { get; private set; }
        public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken) => Task.FromResult(ImageExists);
        public Task<IReadOnlyCollection<ContainerListResponse>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Containers);
        public Task<ContainerInspectResponse> InspectAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Inspection);
        public Task<CreateContainerResponse> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return CreateException is null ? Task.FromResult(new CreateContainerResponse { ID = "created-id" }) : Task.FromException<CreateContainerResponse>(CreateException);
        }
    }
}
