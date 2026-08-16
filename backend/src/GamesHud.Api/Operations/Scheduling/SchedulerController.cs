using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Operations.Scheduling;

[ApiController]
[Route("api/scheduler")]
public sealed class SchedulerController : ControllerBase
{
    private readonly ISchedulerService _schedulerService;
    private readonly ILogger<SchedulerController> _logger;

    public SchedulerController(
        ISchedulerService schedulerService,
        ILogger<SchedulerController> logger)
    {
        _schedulerService = schedulerService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetTasks()
    {
        return Ok(_schedulerService.GetTasks());
    }

    [HttpPost]
    public IActionResult UpsertTask([FromBody] ScheduleTaskRequest request)
    {
        try
        {
            return Ok(_schedulerService.UpsertTask(request));
        }
        catch (SchedulerValidationException exception)
        {
            return Problem(
                title: "Invalid schedule task",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("{id}/run")]
    public async Task<IActionResult> RunTask(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _schedulerService.RunTaskAsync(id, cancellationToken);

            if (!result.Success)
            {
                return Conflict(result);
            }

            return Ok(result);
        }
        catch (SchedulerValidationException exception)
        {
            return Problem(
                title: "Invalid schedule task",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while running scheduled task {TaskId}.", id);

            return Problem(
                title: "Unexpected scheduler error",
                detail: "The API could not run the scheduled task.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
