using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.GameServers.Provisioning;

public static class ProvisioningStepIds
{
    public const string ValidateHost = "validate_host";
    public const string PlanResources = "plan_resources";
    public const string ReserveResources = "reserve_resources";
    public const string PrepareStorage = "prepare_storage";
    public const string ConfigureGame = "configure_game";
    public const string CreateRuntime = "create_runtime";
    public const string StartRuntime = "start_runtime";
    public const string VerifyHealth = "verify_health";
    public const string Complete = "complete";

    public static readonly IReadOnlyCollection<string> All =
    [
        ValidateHost,
        PlanResources,
        ReserveResources,
        PrepareStorage,
        ConfigureGame,
        CreateRuntime,
        StartRuntime,
        VerifyHealth,
        Complete
    ];

    public static readonly IReadOnlyCollection<string> ExecutableFoundation =
    [PrepareStorage, ConfigureGame, CreateRuntime, StartRuntime, VerifyHealth, Complete];
}

public static class ProvisioningErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidGameServerId = "invalid_game_server_id";
    public const string GameNotFound = "game_not_found";
    public const string HostIncompatible = "host_incompatible";
    public const string PortConflict = "port_conflict";
    public const string StorageConflict = "storage_conflict";
    public const string DuplicateServer = "duplicate_server";
    public const string OperationInProgress = "operation_in_progress";
    public const string ReservationFailed = "reservation_failed";
    public const string StepFailed = "step_failed";
    public const string ProvisioningCancelled = "provisioning_cancelled";
}

public static class ProvisioningStepResultStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public sealed record CreateGameServerProvisioningRequest(
    string GameServerId,
    string GameId,
    string DisplayName);

public sealed record ValidatedProvisioningPort(
    string DefinitionId,
    string Protocol,
    int Port,
    string Exposure);

public sealed record ValidatedProvisioningStorage(
    string DefinitionId,
    string RelativePath);

public sealed record ValidatedProvisioningPlan(
    GameServerId GameServerId,
    GameId GameId,
    string DisplayName,
    string RuntimeType,
    string HostCompatibilityStatus,
    IReadOnlyCollection<GameCompatibilityIssue> HostWarnings,
    IReadOnlyCollection<ValidatedProvisioningPort> Ports,
    IReadOnlyCollection<ValidatedProvisioningStorage> Storage,
    IReadOnlyCollection<SecretReference> SecretReferences,
    IReadOnlyCollection<string> RequiredSteps);

public sealed record ProvisioningFailure(string Code, string SafeMessage);

public sealed record ProvisioningPlanBuildResult(
    ValidatedProvisioningPlan? Plan,
    GameDefinition? Definition,
    ProvisioningFailure? Failure)
{
    public bool Succeeded => Plan is not null && Definition is not null && Failure is null;
}

public sealed record ProvisioningPreviewResult(
    bool IsValid,
    ValidatedProvisioningPlan? Plan,
    ProvisioningFailure? Failure);

public sealed record ProvisioningExecutionResult(
    bool Succeeded,
    string? OperationId,
    string Status,
    ProvisioningFailure? Failure);

public sealed record ProvisioningOperationSnapshot(
    string OperationId,
    string GameServerId,
    string Status,
    string CurrentStep,
    string? ErrorCode,
    string? ErrorMessageSafe,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record ProvisioningStepResult(
    string Status,
    string? ErrorCode = null,
    string? SafeMessage = null)
{
    public static ProvisioningStepResult Success() => new(ProvisioningStepResultStatuses.Succeeded);

    public static ProvisioningStepResult Skipped(string message) =>
        new(ProvisioningStepResultStatuses.Skipped, SafeMessage: message);

    public static ProvisioningStepResult Failure(string code, string message) =>
        new(ProvisioningStepResultStatuses.Failed, code, message);
}

public sealed record ProvisioningStepExecution(string StepId, string Status);

public sealed class ProvisioningContext
{
    private readonly List<ProvisioningStepExecution> _executions = [];

    public ProvisioningContext(
        string operationId,
        GameDefinition gameDefinition,
        ValidatedProvisioningPlan validatedPlan,
        ManagedServerReservationResult reservedResources)
    {
        OperationId = operationId;
        GameServerId = validatedPlan.GameServerId;
        GameDefinition = gameDefinition;
        ValidatedPlan = validatedPlan;
        ReservedResources = reservedResources;
    }

    public string OperationId { get; }
    public GameServerId GameServerId { get; }
    public GameDefinition GameDefinition { get; }
    public ValidatedProvisioningPlan ValidatedPlan { get; }
    public ManagedServerReservationResult ReservedResources { get; }
    public IReadOnlyCollection<ProvisioningStepExecution> Executions => _executions;

    public void Record(string stepId, string status) =>
        _executions.Add(new ProvisioningStepExecution(stepId, status));
}
