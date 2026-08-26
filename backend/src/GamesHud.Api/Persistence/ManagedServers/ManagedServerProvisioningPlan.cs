namespace GamesHud.Api.Persistence.ManagedServers;

public sealed record ManagedServerProvisioningPlan(
    string GameServerId,
    string GameId,
    string DisplayName,
    string RuntimeType,
    IReadOnlyCollection<PortReservationPlan> Ports,
    IReadOnlyCollection<StorageReservationPlan> Storage,
    string? PipelineVersion = null,
    IReadOnlyCollection<ProvisioningStepPlan>? Steps = null);

public sealed record PortReservationPlan(
    string PortDefinitionId,
    string Protocol,
    int Port,
    string Exposure);

public sealed record StorageReservationPlan(
    string StorageDefinitionId,
    string? RelativePath = null);

public sealed record ProvisioningStepPlan(
    string StepId,
    int Sequence,
    string RetryClassification,
    string SideEffectClassification,
    int MaxAttempts,
    bool CompletedBeforeReservation = false);

public sealed record ManagedServerReservationResult(
    string GameServerId,
    string ProvisioningOperationId,
    IReadOnlyCollection<string> PortReservationIds,
    IReadOnlyCollection<string> StorageReservationIds);

public sealed record ManagedServerReservationConflict(string Code, string SafeMessage);
