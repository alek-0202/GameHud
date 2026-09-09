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
    private const int MaximumEnvironmentEntries = 32;
    private const int MaximumEnvironmentNameLength = 128;
    private const int MaximumEnvironmentValueLength = 1024;

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
        ValidateEnvironment(specification, definition, violations);

        if (specification.Resources.CpuCount <= 0 || specification.Resources.MemoryBytes == 0)
            Add(violations, RuntimePolicyErrorCodes.ResourceLimitInvalid, "Runtime resource limits are invalid.");

        if (specification.RestartPolicy != RuntimeRestartPolicies.UnlessStopped
            || specification.NetworkPolicy != RuntimeNetworkPolicies.GamesHudManaged)
            Add(violations, RuntimePolicyErrorCodes.UnsafeRuntimeConfiguration, "Runtime configuration is not approved.");

        return violations.Count == 0
            ? new(true, new ValidatedRuntimeMutationSpecification(CreateImmutableSnapshot(specification)), [])
            : new(false, null, violations);
    }

    private static void ValidateEnvironment(RuntimeMutationSpecification specification, GameDefinition definition,
        List<RuntimePolicyViolation> violations)
    {
        var actual = specification.Environment;
        var expected = definition.RuntimeEnvironment
            .Where(item => item.RuntimeType == specification.RuntimeType).ToArray();

        if (actual is null || actual.Count > MaximumEnvironmentEntries
            || actual.Any(item => item is null || !IsValidEnvironmentEntry(item))
            || actual.Where(item => item is not null).GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            Add(violations, RuntimePolicyErrorCodes.RuntimeEnvironmentInvalid,
                "Runtime environment configuration is invalid.");

        if (actual is null || actual.Count != expected.Length
            || !actual.OrderBy(item => item?.Name, StringComparer.Ordinal).SequenceEqual(
                expected.OrderBy(item => item.Name, StringComparer.Ordinal)))
            Add(violations, RuntimePolicyErrorCodes.RuntimeEnvironmentMismatch,
                "Runtime environment does not match the trusted game definition.");
    }

    private static bool IsValidEnvironmentEntry(TrustedRuntimeEnvironmentVariable entry)
    {
        if (string.IsNullOrEmpty(entry.RuntimeType) || string.IsNullOrEmpty(entry.Name) || entry.Value is null
            || entry.Name.Length > MaximumEnvironmentNameLength || entry.Value.Length > MaximumEnvironmentValueLength
            || entry.Name[0] is >= '0' and <= '9'
            || entry.Name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            return false;

        return !entry.Value.Any(character => character == '\0' || character == '\r' || character == '\n'
            || char.IsControl(character));
    }

    private static RuntimeMutationSpecification CreateImmutableSnapshot(RuntimeMutationSpecification specification) =>
        specification with
        {
            Ports = Array.AsReadOnly(specification.Ports.ToArray()),
            Mounts = Array.AsReadOnly(specification.Mounts.ToArray()),
            SecretReferences = Array.AsReadOnly(specification.SecretReferences.ToArray()),
            Environment = Array.AsReadOnly(specification.Environment.ToArray())
        };

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
