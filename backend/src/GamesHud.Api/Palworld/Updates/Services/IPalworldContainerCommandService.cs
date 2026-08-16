namespace GamesHud.Api.Palworld.Updates.Services;

public interface IPalworldContainerCommandService
{
    Task<PalworldContainerCommandResult> ExecuteAsync(
        string containerName,
        IReadOnlyList<string> command,
        CancellationToken cancellationToken);
}
