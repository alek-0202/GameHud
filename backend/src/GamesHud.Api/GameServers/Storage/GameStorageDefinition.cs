namespace GamesHud.Api.GameServers.Storage;

public static class StoragePurposes
{
    public const string GameData = "game_data";
    public const string Backups = "backups";
    public const string Logs = "logs";
}

public static class StorageOwnerships
{
    public const string Managed = "managed";
    public const string External = "external";
}

public sealed record GameStorageDefinition
{
    public GameStorageDefinition(
        string id,
        string label,
        string purpose,
        string? runtimeTarget,
        bool persistent,
        bool required,
        bool backupEligible,
        bool userData,
        ulong? minimumBytes = null,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (minimumBytes is 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumBytes));
        }

        Id = NormalizeIdentifier(id, nameof(id));
        Label = label.Trim();
        Purpose = NormalizeIdentifier(purpose, nameof(purpose));
        RuntimeTarget = string.IsNullOrWhiteSpace(runtimeTarget) ? null : runtimeTarget.Trim();
        Persistent = persistent;
        Required = required;
        BackupEligible = backupEligible;
        UserData = userData;
        MinimumBytes = minimumBytes;
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
    }

    public string Id { get; }

    public string Label { get; }

    public string Purpose { get; }

    public string? RuntimeTarget { get; }

    public bool Persistent { get; }

    public bool Required { get; }

    public bool BackupEligible { get; }

    public bool UserData { get; }

    public ulong? MinimumBytes { get; }

    public string? Source { get; }

    private static string NormalizeIdentifier(string value, string parameterName)
    {
        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new ArgumentException("Storage identifier contains unsupported characters.", parameterName);
        }

        return normalized;
    }
}

public interface IGameStorageDefinition
{
    IReadOnlyCollection<GameStorageDefinition> Storages { get; }
}
