using GamesHud.Api.Docker.Models;
using GamesHud.Api.Docker.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ContainersController : ControllerBase
{
    private readonly IContainerService _containerService;
    private readonly ILogger<ContainersController> _logger;

    public ContainersController(
        IContainerService containerService,
        ILogger<ContainersController> logger)
    {
        _containerService = containerService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetContainers(CancellationToken cancellationToken)
    {
        try
        {
            var containers = await _containerService.GetContainersAsync(cancellationToken);

            return Ok(containers);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable while listing containers.");

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not connect to Docker. Check whether Docker is running and the configured endpoint is accessible.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
