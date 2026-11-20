namespace GamesHud.Api.GameServers.Domain;

public sealed record GameServerRuntime
{
    public const string DockerType = "docker";

    public GameServerRuntime(string type, string externalReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(externalReference);

        Type = type.Trim().ToLowerInvariant();
        ExternalReference = externalReference.Trim();
    }

    public string Type { get; }

    public string ExternalReference { get; }
}
