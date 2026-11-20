namespace GamesHud.Api.Persistence.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public bool AutoMigrate { get; set; } = true;
}
