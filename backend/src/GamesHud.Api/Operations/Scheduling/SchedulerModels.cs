using System.Globalization;
using System.Text.RegularExpressions;

namespace GamesHud.Api.Operations.Scheduling;

public static class ScheduleActionTypes
{
    public const string AutomaticBackup = "automatic-backup";
    public const string RestartPalworld = "restart-palworld";
    public const string UpdateCheck = "update-check";
    public const string Announcement = "announcement";
    public const string ShutdownPalworld = "shutdown-palworld";
}

public sealed record ScheduleTask(
    string Id,
    string ActionType,
    int RecurrenceMinutes,
    bool Enabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string LastResult,
    string Status,
    string? Message)
{
    public ScheduleTaskResponse ToResponse()
    {
        return new ScheduleTaskResponse(
            Id,
            ActionType,
            RecurrenceMinutes,
            Enabled,
            NextRunAt?.ToString("O", CultureInfo.InvariantCulture),
            LastRunAt?.ToString("O", CultureInfo.InvariantCulture),
            LastResult,
            Status,
            Message);
    }
}

public sealed record ScheduledOperationResult(
    bool Success,
    string Result);

public sealed class SchedulerValidationException : Exception
{
    public SchedulerValidationException(string message)
        : base(message)
    {
    }
}

public static class SchedulerValidation
{
    private static readonly Regex IdPattern = new(@"\A[a-z0-9][a-z0-9-]{2,60}\z", RegexOptions.CultureInvariant);

    public static ScheduleTask NormalizeTask(
        ScheduleTaskRequest request,
        TimeProvider timeProvider)
    {
        var id = string.IsNullOrWhiteSpace(request.Id)
            ? CreateTaskId(request.ActionType)
            : request.Id.Trim().ToLowerInvariant();
        var actionType = NormalizeActionType(request.ActionType);

        if (!IdPattern.IsMatch(id))
        {
            throw new SchedulerValidationException("Schedule id is invalid.");
        }

        if (request.RecurrenceMinutes is < 1 or > 10_080)
        {
            throw new SchedulerValidationException("Recurrence must be between 1 and 10080 minutes.");
        }

        var message = NormalizeMessage(request.Message);
        DateTimeOffset? nextRun = request.Enabled
            ? timeProvider.GetUtcNow().AddMinutes(request.RecurrenceMinutes)
            : null;

        return new ScheduleTask(
            id,
            actionType,
            request.RecurrenceMinutes,
            request.Enabled,
            nextRun,
            null,
            "never-run",
            request.Enabled ? "scheduled" : "disabled",
            message);
    }

    public static string NormalizeActionType(string actionType)
    {
        var normalized = actionType.Trim().ToLowerInvariant();

        return normalized switch
        {
            ScheduleActionTypes.AutomaticBackup => normalized,
            ScheduleActionTypes.RestartPalworld => normalized,
            ScheduleActionTypes.UpdateCheck => normalized,
            ScheduleActionTypes.Announcement => normalized,
            ScheduleActionTypes.ShutdownPalworld => normalized,
            _ => throw new SchedulerValidationException("Schedule action type is not supported.")
        };
    }

    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = message.Trim();

        if (normalized.Length > 200 || normalized.Contains('\r', StringComparison.Ordinal))
        {
            throw new SchedulerValidationException("Schedule message must be 200 characters or fewer.");
        }

        return normalized;
    }

    private static string CreateTaskId(string actionType)
    {
        var normalizedActionType = NormalizeActionType(actionType);

        return $"{normalizedActionType}-{Guid.NewGuid():N}"[..32].TrimEnd('-');
    }
}
