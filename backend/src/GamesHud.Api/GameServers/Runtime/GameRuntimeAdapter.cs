using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Provisioning;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Runtime;

public interface IGameRuntimeAdapter
{
    Task<RuntimeProviderOutcome> CreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken);
    Task<RuntimeProviderOutcome> StartAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken);
}

public sealed record RuntimeProviderOutcome(string Status, string? SafeCode = null, string? SafeMessage = null)
{
    public static RuntimeProviderOutcome Success(string message) => new(RuntimeMutationOutcomeStatuses.Success, SafeMessage: message);
    public static RuntimeProviderOutcome KnownFailure(string code, string message) => new(RuntimeMutationOutcomeStatuses.KnownFailure, code, message);
    public static RuntimeProviderOutcome Unknown(string code, string message) => new(RuntimeMutationOutcomeStatuses.Unknown, code, message);
}

public static class DockerRuntimeErrorCodes
{
    public const string ImageUnavailable = "runtime_image_unavailable";
    public const string ProviderUnavailable = "runtime_provider_unavailable";
    public const string IdentityConflict = "runtime_identity_conflict";
    public const string OutcomeUnknown = "runtime_create_outcome_unknown";
    public const string NotFound = "runtime_not_found";
    public const string StartConflict = "runtime_start_conflict";
    public const string StartFailed = "runtime_start_failed";
    public const string StartUnknown = "runtime_start_unknown";
}

internal static class DockerManagedRuntimeLabels
{
    public const string Managed = "gameshud.managed";
    public const string GameServerId = "gameshud.gameServerId";
    public const string GameId = "gameshud.gameId";
    public const string Identity = "gameshud.runtimeIdentity";
}

internal interface IDockerManagedRuntimeClient
{
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ContainerListResponse>> ListAsync(CancellationToken cancellationToken);
    Task<ContainerInspectResponse> InspectAsync(string id, CancellationToken cancellationToken);
    Task<CreateContainerResponse> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken);
    Task<bool> StartAsync(string id, CancellationToken cancellationToken);
}

internal interface IManagedRuntimeStorageValidator
{
    bool IsPreparedAndSafe(IReadOnlyCollection<RuntimeStorageMount> mounts);
}

internal sealed class ManagedRuntimeStorageValidator : IManagedRuntimeStorageValidator
{
    public bool IsPreparedAndSafe(IReadOnlyCollection<RuntimeStorageMount> mounts)
    {
        try
        {
            return mounts.Count > 0 && mounts.All(mount => Directory.Exists(mount.SourcePath)
                && !IsDockerSocket(mount.SourcePath) && !IsDockerSocket(mount.RuntimeTarget)
                && !HasReparsePoint(mount.SourcePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool HasReparsePoint(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static bool IsDockerSocket(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.Equals("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/run/docker.sock", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("//./pipe/docker_engine", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class DockerManagedRuntimeClient : IDockerManagedRuntimeClient
{
    private readonly IOptions<DockerOptions> _options;
    public DockerManagedRuntimeClient(IOptions<DockerOptions> options) => _options = options;

    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        try
        {
            await client.Images.InspectImageAsync(image, cancellationToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyCollection<ContainerListResponse>> ListAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        return (await client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellationToken)).ToArray();
    }

    public async Task<ContainerInspectResponse> InspectAsync(string id, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        return await client.Containers.InspectContainerAsync(id, cancellationToken);
    }

    public async Task<CreateContainerResponse> CreateAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        return await client.Containers.CreateContainerAsync(parameters, cancellationToken);
    }

    public async Task<bool> StartAsync(string id, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        return await client.Containers.StartContainerAsync(id, new ContainerStartParameters(), cancellationToken);
    }

    private IDockerClient CreateClient() => string.IsNullOrWhiteSpace(_options.Value.Endpoint)
        ? new DockerClientConfiguration().CreateClient()
        : new DockerClientConfiguration(new Uri(_options.Value.Endpoint)).CreateClient();
}

internal static class DockerCreateContainerMapper
{
    public static CreateContainerParameters Map(RuntimeMutationExecutionContext context)
    {
        var specification = context.Specification.Value;
        var exposed = new Dictionary<string, EmptyStruct>();
        var bindings = new Dictionary<string, IList<PortBinding>>();
        foreach (var port in specification.Ports)
        {
            var key = $"{port.Port}/{port.Protocol}";
            exposed[key] = default;
            if (port.Exposure == PortExposures.Public)
                bindings[key] = [new PortBinding { HostIP = "", HostPort = port.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) }];
        }

        return new CreateContainerParameters
        {
            Name = context.ProviderResourceIdentity.ResourceName,
            Image = specification.Image.Reference,
            Labels = new Dictionary<string, string>
            {
                [DockerManagedRuntimeLabels.Managed] = "true",
                [DockerManagedRuntimeLabels.GameServerId] = specification.GameServerId.ToString(),
                [DockerManagedRuntimeLabels.GameId] = specification.GameId.ToString(),
                [DockerManagedRuntimeLabels.Identity] = context.ProviderResourceIdentity.OwnershipKey
            },
            ExposedPorts = exposed,
            HostConfig = new HostConfig
            {
                Binds = specification.Mounts.Select(mount => $"{mount.SourcePath}:{mount.RuntimeTarget}:{(mount.ReadOnly ? "ro" : "rw")}").ToList(),
                PortBindings = bindings,
                Privileged = false,
                NetworkMode = "default",
                NanoCPUs = checked(specification.Resources.CpuCount * 1_000_000_000L),
                Memory = checked((long)specification.Resources.MemoryBytes),
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped }
            }
        };
    }
}

internal static class ManagedRuntimeStates
{
    public const string Absent = "absent";
    public const string Created = "created";
    public const string Exited = "exited";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Restarting = "restarting";
    public const string Removing = "removing";
    public const string Dead = "dead";
    public const string Ambiguous = "ambiguous";
}

internal sealed record ManagedRuntimeInspection(string State, string? ContainerId = null, string? DockerHealth = null);

internal interface IManagedRuntimeInspector
{
    Task<ManagedRuntimeInspection> InspectManagedAsync(
        RuntimeMutationExecutionContext context, CancellationToken cancellationToken);
}

internal sealed class DockerGameRuntimeAdapter : IGameRuntimeAdapter, IManagedRuntimeInspector
{
    private readonly IDockerManagedRuntimeClient _client;
    private readonly IManagedRuntimeStorageValidator _storage;
    public DockerGameRuntimeAdapter(IDockerManagedRuntimeClient client, IManagedRuntimeStorageValidator storage)
    {
        _client = client;
        _storage = storage;
    }

    public async Task<RuntimeProviderOutcome> CreateAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var expected = DockerCreateContainerMapper.Map(context);
        if (!_storage.IsPreparedAndSafe(context.Specification.Value.Mounts))
            return RuntimeProviderOutcome.KnownFailure(RuntimePolicyErrorCodes.InvalidMount, "Managed runtime storage is not prepared or safe.");

        try
        {
            if (!await _client.ImageExistsAsync(expected.Image, cancellationToken))
                return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.ImageUnavailable, "The approved runtime image is not available locally.");
            var inspection = await InspectAsync(expected, cancellationToken);
            if (inspection == ProvisioningReconciliationOutcomes.EffectExists)
                return RuntimeProviderOutcome.Success("Managed runtime container already exists.");
            if (inspection == ProvisioningReconciliationOutcomes.Ambiguous)
                return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.IdentityConflict, "The managed runtime identity is conflicting or ambiguous.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.ProviderUnavailable, "Docker could not be inspected before runtime creation.");
        }

        try
        {
            var created = await _client.CreateAsync(expected, cancellationToken);
            return string.IsNullOrWhiteSpace(created.ID)
                ? RuntimeProviderOutcome.Unknown(DockerRuntimeErrorCodes.OutcomeUnknown, "Docker runtime creation outcome is unknown.")
                : RuntimeProviderOutcome.Success("Managed runtime container was created and remains stopped.");
        }
        catch (Exception)
        {
            return RuntimeProviderOutcome.Unknown(DockerRuntimeErrorCodes.OutcomeUnknown, "Docker runtime creation outcome is unknown.");
        }
    }

    public async Task<RuntimeProviderOutcome> StartAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ManagedRuntimeInspection inspection;
        try
        {
            inspection = await InspectManagedAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.ProviderUnavailable,
                "Docker could not be inspected before runtime start.");
        }

        if (inspection.State == ManagedRuntimeStates.Running)
            return RuntimeProviderOutcome.Success("Managed runtime is already running.");
        if (inspection.State == ManagedRuntimeStates.Absent)
            return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.NotFound, "Managed runtime container was not found.");
        if (inspection.State is not ManagedRuntimeStates.Created and not ManagedRuntimeStates.Exited)
            return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.StartConflict,
                "Managed runtime is not in a safe state for start.");

        try
        {
            var accepted = await _client.StartAsync(inspection.ContainerId!, cancellationToken);
            if (!accepted)
                return RuntimeProviderOutcome.KnownFailure(DockerRuntimeErrorCodes.StartFailed, "Docker rejected managed runtime start.");
            var confirmed = await InspectManagedAsync(context, cancellationToken);
            return confirmed.State == ManagedRuntimeStates.Running
                ? RuntimeProviderOutcome.Success("Managed runtime is running.")
                : RuntimeProviderOutcome.Unknown(DockerRuntimeErrorCodes.StartUnknown,
                    "Managed runtime start outcome is unknown.");
        }
        catch (Exception)
        {
            return RuntimeProviderOutcome.Unknown(DockerRuntimeErrorCodes.StartUnknown,
                "Managed runtime start outcome is unknown.");
        }
    }

    public async Task<ManagedRuntimeInspection> InspectManagedAsync(
        RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        var expected = DockerCreateContainerMapper.Map(context);
        var containers = await _client.ListAsync(cancellationToken);
        var identity = Classify(containers, expected);
        if (identity == ProvisioningReconciliationOutcomes.EffectAbsent)
            return new(ManagedRuntimeStates.Absent);
        if (identity == ProvisioningReconciliationOutcomes.Ambiguous)
            return new(ManagedRuntimeStates.Ambiguous);
        var expectedName = "/" + expected.Name;
        var candidate = containers.Single(container => container.Names?.Contains(expectedName, StringComparer.Ordinal) == true);
        var actual = await _client.InspectAsync(candidate.ID, cancellationToken);
        if (!CriticalConfigurationMatches(actual, expected)) return new(ManagedRuntimeStates.Ambiguous);
        var state = NormalizeState(actual.State);
        return new(state, candidate.ID, actual.State?.Health?.Status);
    }

    private static string NormalizeState(ContainerState? state)
    {
        if (state is null) return ManagedRuntimeStates.Ambiguous;
        if (state.Paused) return ManagedRuntimeStates.Paused;
        if (state.Restarting) return ManagedRuntimeStates.Restarting;
        if (state.Dead) return ManagedRuntimeStates.Dead;
        if (state.Running) return ManagedRuntimeStates.Running;
        return state.Status switch
        {
            "created" => ManagedRuntimeStates.Created,
            "exited" => ManagedRuntimeStates.Exited,
            "removing" => ManagedRuntimeStates.Removing,
            "dead" => ManagedRuntimeStates.Dead,
            _ => ManagedRuntimeStates.Ambiguous
        };
    }

    public async Task<ProvisioningReconciliationResult> ReconcileAsync(RuntimeMutationExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var outcome = await InspectAsync(DockerCreateContainerMapper.Map(context), cancellationToken);
            return new(outcome, outcome switch
            {
                ProvisioningReconciliationOutcomes.EffectExists => "Managed runtime container exists and matches the expected stopped configuration.",
                ProvisioningReconciliationOutcomes.EffectAbsent => "Managed runtime container is absent.",
                _ => "Managed runtime container state could not be proven safely."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(ProvisioningReconciliationOutcomes.Ambiguous, "Managed runtime container state could not be proven safely.");
        }
    }

    private async Task<string> InspectAsync(CreateContainerParameters expected, CancellationToken cancellationToken)
    {
        var containers = await _client.ListAsync(cancellationToken);
        var initial = Classify(containers, expected);
        if (initial != ProvisioningReconciliationOutcomes.EffectExists) return initial;
        var expectedName = "/" + expected.Name;
        var candidate = containers.Single(container => container.Names?.Contains(expectedName, StringComparer.Ordinal) == true);
        var inspected = await _client.InspectAsync(candidate.ID, cancellationToken);
        return CriticalConfigurationMatches(inspected, expected)
            && inspected.State is not null && !inspected.State.Running
            && !inspected.State.Paused && !inspected.State.Restarting && !inspected.State.Dead
            ? ProvisioningReconciliationOutcomes.EffectExists
            : ProvisioningReconciliationOutcomes.Ambiguous;
    }

    internal static bool CriticalConfigurationMatches(ContainerInspectResponse actual, CreateContainerParameters expected)
    {
        if (actual.Config?.Image != expected.Image || actual.HostConfig is null) return false;
        if (actual.HostConfig.Privileged || actual.HostConfig.NetworkMode != expected.HostConfig.NetworkMode
            || actual.HostConfig.NanoCPUs != expected.HostConfig.NanoCPUs || actual.HostConfig.Memory != expected.HostConfig.Memory
            || actual.HostConfig.RestartPolicy?.Name != expected.HostConfig.RestartPolicy.Name) return false;
        var actualBinds = (actual.HostConfig.Binds ?? []).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expectedBinds = (expected.HostConfig.Binds ?? []).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!actualBinds.SequenceEqual(expectedBinds, StringComparer.Ordinal)) return false;
        var actualPorts = actual.HostConfig.PortBindings ?? new Dictionary<string, IList<PortBinding>>();
        return expected.HostConfig.PortBindings.Count == actualPorts.Count && expected.HostConfig.PortBindings.All(binding =>
            actualPorts.TryGetValue(binding.Key, out var values)
            && values.Select(value => (value.HostIP ?? "", value.HostPort)).SequenceEqual(
                binding.Value.Select(value => (value.HostIP ?? "", value.HostPort))));
    }

    internal static string Classify(IReadOnlyCollection<ContainerListResponse> containers, CreateContainerParameters expected)
    {
        var expectedName = "/" + expected.Name;
        var identityMatches = containers.Where(container => container.Labels is not null
            && container.Labels.TryGetValue(DockerManagedRuntimeLabels.Identity, out var identity)
            && identity == expected.Labels[DockerManagedRuntimeLabels.Identity]).ToArray();
        var nameMatches = containers.Where(container => container.Names?.Contains(expectedName, StringComparer.Ordinal) == true).ToArray();
        var candidates = identityMatches.Concat(nameMatches).DistinctBy(container => container.ID).ToArray();
        if (candidates.Length == 0) return ProvisioningReconciliationOutcomes.EffectAbsent;
        if (candidates.Length != 1) return ProvisioningReconciliationOutcomes.Ambiguous;
        var candidate = candidates[0];
        var labels = candidate.Labels;
        var owned = labels is not null
            && labels.TryGetValue(DockerManagedRuntimeLabels.Managed, out var managed) && managed == "true"
            && expected.Labels.All(label => labels.TryGetValue(label.Key, out var value) && value == label.Value);
        var named = candidate.Names?.Contains(expectedName, StringComparer.Ordinal) == true;
        var image = candidate.Image == expected.Image;
        return owned && named && image
            ? ProvisioningReconciliationOutcomes.EffectExists
            : ProvisioningReconciliationOutcomes.Ambiguous;
    }
}
