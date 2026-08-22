namespace GamesHud.Api.GameServers.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataRoot { get; set; } = string.Empty;
}
