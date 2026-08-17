using GamesHud.Api.Palworld.Configuration;

namespace GamesHud.Api.GameServers.Configuration;

public sealed class GameServerOptions
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string ManagedPath { get; set; } = string.Empty;

    public string BackupPath { get; set; } = string.Empty;

    public string ConnectionAddress { get; set; } = string.Empty;

    public string BrandingImage { get; set; } = string.Empty;

    public PalworldRestApiOptions RestApi { get; set; } = new();

    public PalworldBackupOptions Backups { get; set; } = new();
}
