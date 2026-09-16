using GamesHud.Api.GameServers.Configuration;

namespace GamesHud.Api.Persistence.ManagedServers;

public sealed record ManagedServerProvisioningPlan(
    string GameServerId,
    string GameId,
    string DisplayName,
    string RuntimeType,
    IReadOnlyCollection<PortReservationPlan> Ports,
    IReadOnlyCollection<StorageReservationPlan> Storage,
    ValidatedGameProvisioningConfiguration? Configuration = null,
    string? PipelineVersion = null,
    IReadOnlyCollection<ProvisioningStepPlan>? Steps = null,
    GamesHud.Api.GameServers.Definitions.TrustedRuntimeImage? RuntimeImage = null);

public sealed record PortReservationPlan(
    string PortDefinitionId,
    string Protocol,
    int ContainerPort,
    int? HostPort,
    bool Published,
    string Exposure)
{
    public PortReservationPlan(string portDefinitionId, string protocol, int port, string exposure)
        : this(portDefinitionId, protocol, port,
            exposure == GameServers.Ports.PortExposures.Public ? port : null,
            exposure == GameServers.Ports.PortExposures.Public,
            exposure) { }
}

public sealed record StorageReservationPlan(
    string StorageDefinitionId,
    string? RelativePath = null,
    string? ApiPath = null,
    string? HostPath = null);

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
