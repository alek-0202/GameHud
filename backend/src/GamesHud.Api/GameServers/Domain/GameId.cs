namespace GamesHud.Api.GameServers.Domain;

public readonly struct GameId : IEquatable<GameId>
{
    public GameId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public bool Equals(GameId other)
    {
        return StringComparer.Ordinal.Equals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is GameId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static bool operator ==(GameId left, GameId right) => left.Equals(right);

    public static bool operator !=(GameId left, GameId right) => !left.Equals(right);
}
