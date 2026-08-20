namespace GamesHud.Api.GameServers.Domain;

public readonly struct GameServerId : IEquatable<GameServerId>
{
    public GameServerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public bool Equals(GameServerId other)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is GameServerId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    public static bool operator ==(GameServerId left, GameServerId right) => left.Equals(right);

    public static bool operator !=(GameServerId left, GameServerId right) => !left.Equals(right);
}
