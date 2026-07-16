namespace GamesHud.Api.Configuration;

public sealed class DockerOptions
{
    public const string SectionName = "Docker";

    public string? Endpoint { get; init; }
}
