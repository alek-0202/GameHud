using GamesHud.Api.Docker.Models;
using GamesHud.Api.Palworld.Backups.Contracts;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc;

namespace GamesHud.Api.Palworld.Backups.Controllers;

[ApiController]
[Route("api/palworld/backups")]
public sealed class PalworldBackupsController : ControllerBase
{
    private readonly IPalworldBackupService _backupService;
    private readonly ILogger<PalworldBackupsController> _logger;

    public PalworldBackupsController(
        IPalworldBackupService backupService,
        ILogger<PalworldBackupsController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetBackups(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _backupService.GetBackupsAsync(cancellationToken);

            return Ok(PalworldBackupContractMapper.Map(summary));
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while reading Palworld backups.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpGet("{backupId}")]
    public async Task<IActionResult> GetBackup(
        string backupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);

            return backup is null
                ? BackupNotFoundProblem()
                : Ok(PalworldBackupContractMapper.Map(backup));
        }
        catch (PalworldBackupValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while reading Palworld backup metadata.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpGet("{backupId}/download")]
    public async Task<IActionResult> DownloadBackup(
        string backupId,
        CancellationToken cancellationToken)
    {
        try
        {
            var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);

            if (backup is null)
            {
                return BackupNotFoundProblem();
            }

            var archivePath = await _backupService.GetBackupFilePathAsync(backupId, cancellationToken);
            var stream = System.IO.File.OpenRead(archivePath);

            return File(stream, "application/gzip", backup.Filename);
        }
        catch (PalworldBackupNotFoundException)
        {
            return BackupNotFoundProblem();
        }
        catch (PalworldBackupValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while downloading Palworld backup.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateBackup(
        [FromBody] PalworldCreateBackupRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var backup = await _backupService.CreateBackupAsync(
                new PalworldBackupCreateOptions(
                    PalworldBackupTypes.Manual,
                    request?.Note,
                    RequestWorldSave: true),
                cancellationToken);

            return Ok(new PalworldCreateBackupResponse(
                "Palworld backup created.",
                PalworldBackupContractMapper.Map(backup)));
        }
        catch (PalworldBackupValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (PalworldBackupWriteException exception)
        {
            _logger.LogError(exception, "Palworld backup creation failed.");

            return Problem(
                title: "Palworld backup could not be created",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while creating Palworld backup.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpPost("{backupId}/restore")]
    public async Task<IActionResult> RestoreBackup(
        string backupId,
        [FromBody] PalworldRestoreBackupRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequestProblem("Restore confirmation is required.");
        }

        try
        {
            var result = await _backupService.RestoreBackupAsync(
                backupId,
                request.ConfirmationText,
                cancellationToken);

            return Ok(new PalworldRestoreBackupResponse(
                "Palworld backup restored.",
                result.RestoredBackupId,
                PalworldBackupContractMapper.Map(result.PreRestoreBackup),
                result.PlayersOnlineBeforeRestore,
                result.StopStatus,
                result.StartStatus,
                result.HealthCheckStatus,
                result.CompletedAt.ToString("O")));
        }
        catch (PalworldBackupNotFoundException)
        {
            return BackupNotFoundProblem();
        }
        catch (PalworldBackupValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (DockerUnavailableException exception)
        {
            _logger.LogWarning(exception, "Docker Engine is unavailable during Palworld restore.");

            return Problem(
                title: "Docker Engine is unavailable",
                detail: "The API could not reach Docker Engine to control the configured Palworld container.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (PalworldBackupLifecycleException exception)
        {
            _logger.LogWarning(exception, "Configured Palworld container lifecycle action failed during restore.");

            return Problem(
                title: "Palworld container action failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (PalworldBackupRestoreException exception)
        {
            _logger.LogError(exception, "Palworld backup restore failed.");

            return Problem(
                title: "Palworld backup could not be restored",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while restoring Palworld backup.");

            return UnexpectedErrorProblem();
        }
    }

    [HttpDelete("{backupId}")]
    public async Task<IActionResult> DeleteBackup(
        string backupId,
        [FromBody] PalworldDeleteBackupRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequestProblem("Delete confirmation is required.");
        }

        try
        {
            await _backupService.DeleteBackupAsync(
                backupId,
                request.ConfirmationText,
                cancellationToken);

            return Ok(new PalworldDeleteBackupResponse(
                "Palworld backup deleted.",
                backupId,
                DateTimeOffset.UtcNow.ToString("O")));
        }
        catch (PalworldBackupNotFoundException)
        {
            return BackupNotFoundProblem();
        }
        catch (PalworldBackupValidationException exception)
        {
            return BadRequestProblem(exception.Message);
        }
        catch (PalworldBackupConfigurationException exception)
        {
            _logger.LogWarning(exception, "Palworld backups are not configured.");

            return BackupUnavailableProblem(exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected error while deleting Palworld backup.");

            return UnexpectedErrorProblem();
        }
    }

    private ObjectResult BackupUnavailableProblem(string detail)
    {
        return Problem(
            title: "Palworld backups are not configured",
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private ObjectResult BackupNotFoundProblem()
    {
        return Problem(
            title: "Palworld backup was not found",
            detail: "The requested Palworld backup does not exist.",
            statusCode: StatusCodes.Status404NotFound);
    }

    private ObjectResult BadRequestProblem(string detail)
    {
        return Problem(
            title: "Invalid Palworld backup request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private ObjectResult UnexpectedErrorProblem()
    {
        return Problem(
            title: "Unexpected API error",
            detail: "The API could not complete the Palworld backup request.",
            statusCode: StatusCodes.Status500InternalServerError);
    }
}
