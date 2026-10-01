namespace GamesHud.Api.Authentication;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";
    public string DataProtectionKeysPath { get; set; } = string.Empty;
}
