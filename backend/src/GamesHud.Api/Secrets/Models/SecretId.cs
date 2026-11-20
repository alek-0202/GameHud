namespace GamesHud.Api.Secrets.Models;

public readonly record struct SecretId
{
    public SecretId(string value)
    {
        if (!Guid.TryParseExact(value, "N", out _))
        {
            throw new ArgumentException("Secret id must be an opaque GUID value.", nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public static SecretId New()
    {
        return new SecretId(Guid.NewGuid().ToString("N"));
    }

    public static SecretId Parse(string value)
    {
        return new SecretId(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
