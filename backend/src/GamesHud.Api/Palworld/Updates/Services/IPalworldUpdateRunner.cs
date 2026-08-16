namespace GamesHud.Api.Palworld.Updates.Services;

public interface IPalworldUpdateRunner
{
    Task<string> PrepareUpdateAsync(
        string containerName,
        CancellationToken cancellationToken);
}
