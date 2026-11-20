namespace GamesHud.Api.Secrets.Models;

public readonly record struct SecretPurpose
{
    public const string GameServerPassword = "game_server_password";
    public const string GameAdminPassword = "game_admin_password";
    public const string GameRestCredential = "game_rest_credential";
    public const string Webhook = "webhook";
    public const string IntegrationToken = "integration_token";

    public SecretPurpose(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 128 || value.Any(character =>
            !char.IsAsciiLetterLower(character)
            && !char.IsAsciiDigit(character)
            && character is not '_' and not '-' and not ':'))
        {
            throw new ArgumentException("Secret purpose contains unsupported characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static SecretPurpose From(string value)
    {
        return new SecretPurpose(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
