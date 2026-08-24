namespace GamesHud.Api.Persistence.Models;

public sealed class PersistenceMetadataRecord
{
    public const string SchemaMetadataId = "schema";

    public string Id { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
