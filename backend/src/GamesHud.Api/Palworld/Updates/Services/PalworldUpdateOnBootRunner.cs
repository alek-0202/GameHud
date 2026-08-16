namespace GamesHud.Api.Palworld.Updates.Services;

public sealed class PalworldUpdateOnBootRunner : IPalworldUpdateRunner
{
    public Task<string> PrepareUpdateAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        return Task.FromResult("update-on-boot");
    }
}
