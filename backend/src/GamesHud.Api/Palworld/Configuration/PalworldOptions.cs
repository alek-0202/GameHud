namespace GamesHud.Api.Palworld.Configuration;

public sealed class PalworldOptions
{
    public const string SectionName = "Palworld";

    public string ManagedPath { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;
}
