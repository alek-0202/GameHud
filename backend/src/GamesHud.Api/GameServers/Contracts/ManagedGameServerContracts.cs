namespace GamesHud.Api.GameServers.Contracts;

public sealed record CreateManagedGameServerRequest(string? GameId, string? DisplayName);

public sealed record CreateManagedGameServerResponse(string GameServerId, string ProvisioningOperationId);

public sealed record ManagedGameServerResponse(
    string Id, string GameId, string DisplayName, string InstallationType,
    string RuntimeType, string LifecycleState, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record ManagedProvisioningResponse(
    string OperationId, string GameServerId, string Status, string CurrentStep,
    string PipelineVersion, DateTimeOffset StartedAtUtc, DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc, bool RequiresInspection, ManagedProvisioningFailureResponse? Failure,
    IReadOnlyCollection<ManagedProvisioningStepResponse> Steps);

public sealed record ManagedProvisioningStepResponse(
    string StepId, int Sequence, string Status, int Attempt, int MaxAttempts,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record ManagedProvisioningFailureResponse(string Code, string Message, bool RequiresInspection);

public sealed record ManagedGameServerApiError(string Code, string Message);
