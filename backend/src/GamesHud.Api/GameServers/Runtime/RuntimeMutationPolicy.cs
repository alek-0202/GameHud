using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Storage;

namespace GamesHud.Api.GameServers.Runtime;

public interface IRuntimeMutationPolicy
{
    RuntimeMutationPolicyResult Validate(RuntimeMutationSpecification specification, GameDefinition definition, string managedDataRoot);
}

public sealed class RuntimeMutationPolicy : IRuntimeMutationPolicy
{
    public RuntimeMutationPolicyResult Validate(RuntimeMutationSpecification specification, GameDefinition definition, string managedDataRoot)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(definition);
        var violations = new List<RuntimePolicyViolation>();

        if (!definition.SupportedRuntimes.Contains(specification.RuntimeType, StringComparer.Ordinal))
            Add(violations, RuntimePolicyErrorCodes.UnknownRuntime, "The selected runtime is not supported.");

        var trustedImage = definition.RuntimeImages.SingleOrDefault(image => image.RuntimeType == specification.RuntimeType);
        if (trustedImage is null || trustedImage != specification.Image)
            Add(violations, RuntimePolicyErrorCodes.UntrustedRuntimeImage, "The runtime image is not approved.");

        ValidateMounts(specification, definition, managedDataRoot, violations);
        ValidatePorts(specification, definition, violations);

        if (specification.Resources.CpuCount <= 0 || specification.Resources.MemoryBytes == 0)
            Add(violations, RuntimePolicyErrorCodes.ResourceLimitInvalid, "Runtime resource limits are invalid.");

        if (specification.RestartPolicy != RuntimeRestartPolicies.UnlessStopped
            || specification.NetworkPolicy != RuntimeNetworkPolicies.GamesHudManaged)
            Add(violations, RuntimePolicyErrorCodes.UnsafeRuntimeConfiguration, "Runtime configuration is not approved.");

        return violations.Count == 0
            ? new(true, new ValidatedRuntimeMutationSpecification(specification), [])
            : new(false, null, violations);
    }

    private static void ValidateMounts(RuntimeMutationSpecification specification, GameDefinition definition, string dataRoot, List<RuntimePolicyViolation> violations)
    {
        var root = Path.GetFullPath(dataRoot);
        foreach (var mount in specification.Mounts)
        {
            var storage = definition.Storages.SingleOrDefault(item => item.Id == mount.DefinitionId);
            if (storage is null || storage.RuntimeTarget is null || storage.RuntimeTarget != mount.RuntimeTarget)
            {
                Add(violations, RuntimePolicyErrorCodes.InvalidMount, "A runtime mount does not match the game definition.");
                continue;
            }

            try
            {
                var contained = ManagedStoragePathBuilder.EnsureContained(root, mount.SourcePath, "Runtime storage escaped the managed data root.");
                if (IsSensitivePath(contained) || contained.Equals(root, StringComparison.OrdinalIgnoreCase))
                    Add(violations, RuntimePolicyErrorCodes.InvalidMount, "A runtime mount source is not allowed.");
            }
            catch (Exception exception) when (exception is ArgumentException or StoragePlanningException or NotSupportedException)
            {
                Add(violations, RuntimePolicyErrorCodes.InvalidMount, "A runtime mount source is not allowed.");
            }
        }
    }

    private static void ValidatePorts(RuntimeMutationSpecification specification, GameDefinition definition, List<RuntimePolicyViolation> violations)
    {
        foreach (var binding in specification.Ports)
        {
            var port = definition.Ports.SingleOrDefault(item => item.Id == binding.DefinitionId);
            if (port is null || port.DefaultPort.Protocol != binding.Protocol || port.Exposure != binding.Exposure
                || (!port.AllowAlternative && port.DefaultPort.Number != binding.Port))
                Add(violations, RuntimePolicyErrorCodes.PortReservationMismatch, "A runtime port does not match the game definition.");
        }
    }

    private static bool IsSensitivePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.Equals("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/etc", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/etc/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/proc", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/proc/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/sys", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/sys/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("/dev", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase);
    }

    private static void Add(List<RuntimePolicyViolation> violations, string code, string message)
    {
        if (!violations.Any(item => item.Code == code)) violations.Add(new(code, message));
    }
}
