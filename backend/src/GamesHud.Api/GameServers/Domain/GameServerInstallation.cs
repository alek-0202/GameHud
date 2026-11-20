namespace GamesHud.Api.GameServers.Domain;

public enum GameServerInstallationType
{
    LegacyExternal,
    Managed
}

public sealed record GameServerInstallation(GameServerInstallationType Type);
