namespace GamesHud.Api.Secrets.Models;

public sealed class SecretValue
{
    private readonly string _plainText;

    private SecretValue(string plainText)
    {
        _plainText = plainText;
    }

    public bool IsEmpty => _plainText.Length == 0;

    public static SecretValue FromPlainText(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        if (plainText.Length == 0)
        {
            throw new ArgumentException("Secret value cannot be empty.", nameof(plainText));
        }

        return new SecretValue(plainText);
    }

    public string Reveal()
    {
        return _plainText;
    }

    public override string ToString()
    {
        return "[redacted secret]";
    }
}
