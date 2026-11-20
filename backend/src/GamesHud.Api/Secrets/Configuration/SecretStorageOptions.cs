namespace GamesHud.Api.Secrets.Configuration;

public sealed class SecretStorageOptions
{
    public const string SectionName = "Secrets";

    public string MasterKey { get; set; } = string.Empty;
}
