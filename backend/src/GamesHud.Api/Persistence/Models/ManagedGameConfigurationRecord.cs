namespace GamesHud.Api.Persistence.Models;

public sealed class ManagedGameConfigurationRecord
{
    public string Id { get; set; } = string.Empty;
    public string GameServerId { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public string ConfigurationKind { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string Payload { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ManagedGameServerRecord? GameServer { get; set; }
}
