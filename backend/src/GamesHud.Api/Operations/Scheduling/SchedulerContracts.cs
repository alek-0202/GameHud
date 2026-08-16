namespace GamesHud.Api.Operations.Scheduling;

public sealed record ScheduleTaskResponse(
    string Id,
    string ActionType,
    int RecurrenceMinutes,
    bool Enabled,
    string? NextRunAt,
    string? LastRunAt,
    string LastResult,
    string Status,
    string? Message);

public sealed record ScheduleTaskRequest(
    string? Id,
    string ActionType,
    int RecurrenceMinutes,
    bool Enabled,
    string? Message);

public sealed record ScheduleRunResponse(
    string Id,
    bool Success,
    string Result,
    string CompletedAt);
