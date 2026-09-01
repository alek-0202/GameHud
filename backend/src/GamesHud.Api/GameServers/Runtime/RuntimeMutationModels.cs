using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.GameServers.Runtime;

public static class RuntimeRestartPolicies
{
    public const string UnlessStopped = "unless_stopped";
}

public static class RuntimeNetworkPolicies
{
    public const string GamesHudManaged = "gameshud_managed";
}

public sealed record RuntimePortBinding(string ReservationId, string DefinitionId, string Protocol, int Port, string Exposure);
public sealed record RuntimeStorageMount(string ReservationId, string DefinitionId, string SourcePath, string RuntimeTarget, bool ReadOnly);
public sealed record RuntimeResourceLimits(int CpuCount, ulong MemoryBytes);

public sealed record RuntimeMutationSpecification(
    GameServerId GameServerId,
    GameId GameId,
    string OperationId,
    string RuntimeType,
    TrustedRuntimeImage Image,
    IReadOnlyCollection<RuntimePortBinding> Ports,
    IReadOnlyCollection<RuntimeStorageMount> Mounts,
    IReadOnlyCollection<SecretReference> SecretReferences,
    RuntimeResourceLimits Resources,
    string RestartPolicy,
    string NetworkPolicy);

public sealed class ValidatedRuntimeMutationSpecification
{
    internal ValidatedRuntimeMutationSpecification(RuntimeMutationSpecification value) => Value = value;
    public RuntimeMutationSpecification Value { get; }
}

public sealed record RuntimePolicyViolation(string Code, string SafeMessage);

public sealed record RuntimeMutationPolicyResult(
    bool Allowed,
    ValidatedRuntimeMutationSpecification? Specification,
    IReadOnlyCollection<RuntimePolicyViolation> Violations);

public static class RuntimePolicyErrorCodes
{
    public const string RuntimePolicyDenied = "runtime_policy_denied";
    public const string UnknownRuntime = "unknown_runtime";
    public const string UntrustedRuntimeImage = "untrusted_runtime_image";
    public const string InvalidMount = "invalid_mount";
    public const string ExternalStorageNotAllowed = "external_storage_not_allowed";
    public const string PortReservationMismatch = "port_reservation_mismatch";
    public const string UnsafeRuntimeConfiguration = "unsafe_runtime_configuration";
    public const string ResourceLimitInvalid = "resource_limit_invalid";
}
