using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Runtime;

public enum RuntimeMutationKind { CreateRuntime, StartRuntime }

public static class RuntimeMutationOutcomeStatuses
{
    public const string Success = "success";
    public const string KnownFailure = "known_failure";
    public const string Unknown = "unknown";
    public const string CancelledBeforeInvocation = "cancelled_before_invocation";
}

public static class RuntimeMutationExecutionErrorCodes
{
    public const string ProviderFailure = "runtime_provider_failure";
    public const string ProviderOutcomeUnknown = "runtime_provider_outcome_unknown";
    public const string CancelledBeforeInvocation = "runtime_mutation_cancelled";
}

public sealed record RuntimeProviderResourceIdentity(string ResourceName, string OwnershipKey)
{
    public static RuntimeProviderResourceIdentity For(GameServerId gameServerId) => new($"gameshud-{gameServerId}", gameServerId.ToString());
}

public sealed class RuntimeMutationExecutionContext
{
    public RuntimeMutationExecutionContext(ValidatedRuntimeMutationSpecification specification, RuntimeMutationKind mutationKind, string stepId, int attempt)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        if (attempt <= 0) throw new ArgumentOutOfRangeException(nameof(attempt));
        Specification = specification;
        MutationKind = mutationKind;
        StepId = stepId.Trim();
        Attempt = attempt;
        MutationExecutionId = $"{specification.Value.OperationId}:{StepId}:{mutationKind}";
        ProviderResourceIdentity = RuntimeProviderResourceIdentity.For(specification.Value.GameServerId);
    }

    public ValidatedRuntimeMutationSpecification Specification { get; }
    public RuntimeMutationKind MutationKind { get; }
    public string StepId { get; }
    public int Attempt { get; }
    public string MutationExecutionId { get; }
    public RuntimeProviderResourceIdentity ProviderResourceIdentity { get; }
}

public sealed record RuntimeMutationExecutionResult(string Status, string? SafeCode, string? SafeMessage,
    string RetryClassification, bool ReconciliationRequired, bool ProviderInvoked, string MutationExecutionId);
