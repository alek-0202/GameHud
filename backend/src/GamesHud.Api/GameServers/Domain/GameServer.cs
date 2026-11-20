namespace GamesHud.Api.GameServers.Domain;

public sealed record GameServer
{
    public GameServer(
        GameServerId id,
        GameId gameId,
        string displayName,
        GameServerRuntime runtime,
        GameServerInstallation installation)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Game server id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(gameId.Value))
        {
            throw new ArgumentException("Game id is required.", nameof(gameId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(installation);

        Id = id;
        GameId = gameId;
        DisplayName = displayName.Trim();
        Runtime = runtime;
        Installation = installation;
    }

    public GameServerId Id { get; }

    public GameId GameId { get; }

    public string DisplayName { get; }

    public GameServerRuntime Runtime { get; }

    public GameServerInstallation Installation { get; }
}
