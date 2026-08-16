namespace GamesHud.Api.Palworld.Configuration;

public sealed class PalworldOptions
{
    public const string SectionName = "Palworld";

    public string ManagedPath { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string ConnectionAddress { get; set; } = string.Empty;

    public PalworldRestApiOptions RestApi { get; set; } = new();
}

public sealed class PalworldRestApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;
}
