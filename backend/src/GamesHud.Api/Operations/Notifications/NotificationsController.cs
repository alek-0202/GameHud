using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Operations.Notifications;

[ApiController]
[Route("api/settings/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetSettings()
    {
        return Ok(_notificationService.GetSettings());
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _notificationService.SendTestAsync(cancellationToken);
            var response = new NotificationTestResponse(
                result.Success,
                result.Message,
                result.CompletedAt.ToString("O"));

            if (!result.Success)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
            }

            return Ok(response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while sending Discord test notification.");

            return Problem(
                title: "Unexpected notification error",
                detail: "The API could not send the test notification.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
