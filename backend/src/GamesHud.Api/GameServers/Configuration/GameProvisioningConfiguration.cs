using System.Text.Json;
using System.Text.Json.Serialization;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.GameServers.Configuration;

public static class GameConfigurationErrorCodes
{
    public const string Missing = "game_configuration_missing";
    public const string Invalid = "game_configuration_invalid";
    public const string VersionUnsupported = "game_configuration_version_unsupported";
    public const string Conflict = "game_configuration_conflict";
    public const string PersistenceFailed = "game_configuration_persistence_failed";
}

public sealed record PalworldInitialProvisioningConfiguration(
    string ServerName,
    string ServerDescription,
    int MaxPlayers,
    string Difficulty,
    SecretReference? ServerPasswordSecretReference,
    SecretReference? AdminPasswordSecretReference);

public sealed class ValidatedGameProvisioningConfiguration
{
    internal ValidatedGameProvisioningConfiguration(GameId gameId, string kind, int schemaVersion,
        string payload, object typedValue)
    {
        GameId = gameId;
        ConfigurationKind = kind;
        SchemaVersion = schemaVersion;
        Payload = payload;
        TypedValue = typedValue;
    }

    public GameId GameId { get; }
    public string ConfigurationKind { get; }
    public int SchemaVersion { get; }
    internal string Payload { get; }
    internal object TypedValue { get; }
}

public interface IGameProvisioningConfigurationCodec
{
    GameId GameId { get; }
    string ConfigurationKind { get; }
    int SchemaVersion { get; }
    ValidatedGameProvisioningConfiguration CreateInitial(string serverName,
        SecretReference? serverPassword = null, SecretReference? adminPassword = null);
    ValidatedGameProvisioningConfiguration Deserialize(string gameId, string kind, int schemaVersion, string payload);
}

public sealed class PalworldProvisioningConfigurationCodec : IGameProvisioningConfigurationCodec
{
    public const string InitialConfigurationKind = "palworld.initial";
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public GameId GameId => new("palworld");
    public string ConfigurationKind => InitialConfigurationKind;
    public int SchemaVersion => CurrentSchemaVersion;

    public ValidatedGameProvisioningConfiguration CreateInitial(string serverName,
        SecretReference? serverPassword = null, SecretReference? adminPassword = null) =>
        ValidateAndWrap(new(serverName, string.Empty, 32, "None", serverPassword, adminPassword));

    public ValidatedGameProvisioningConfiguration Deserialize(string gameId, string kind, int schemaVersion, string payload)
    {
        if (!string.Equals(gameId, GameId.Value, StringComparison.Ordinal))
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Game configuration does not match the managed game.");
        if (!string.Equals(kind, ConfigurationKind, StringComparison.Ordinal))
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Game configuration kind is invalid.");
        if (schemaVersion != SchemaVersion)
            throw new GameConfigurationException(GameConfigurationErrorCodes.VersionUnsupported, "Game configuration version is not supported.");
        try
        {
            var dto = JsonSerializer.Deserialize<Payload>(payload, JsonOptions)
                ?? throw new JsonException();
            var value = new PalworldInitialProvisioningConfiguration(dto.ServerName, dto.ServerDescription,
                dto.MaxPlayers, dto.Difficulty,
                ParseReference(dto.ServerPasswordSecretId), ParseReference(dto.AdminPasswordSecretId));
            return ValidateAndWrap(value);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Persisted game configuration is invalid.");
        }
    }

    private ValidatedGameProvisioningConfiguration ValidateAndWrap(PalworldInitialProvisioningConfiguration value)
    {
        if (string.IsNullOrWhiteSpace(value.ServerName) || value.ServerName.Length > 200
            || value.ServerName.Any(char.IsControl) || value.ServerName.Contains('\n') || value.ServerName.Contains('\r')
            || value.ServerDescription.Length > 500 || value.ServerDescription.Any(char.IsControl)
            || value.MaxPlayers is < 1 or > 32 || value.Difficulty is not "None" and not "Normal" and not "Hard")
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Game configuration is invalid.");
        var normalized = value with { ServerName = value.ServerName.Trim(), ServerDescription = value.ServerDescription.Trim() };
        var payload = JsonSerializer.Serialize(new Payload(normalized.ServerName, normalized.ServerDescription,
            normalized.MaxPlayers, normalized.Difficulty, normalized.ServerPasswordSecretReference?.Id.Value,
            normalized.AdminPasswordSecretReference?.Id.Value), JsonOptions);
        return new(GameId, ConfigurationKind, SchemaVersion, payload, normalized);
    }

    private static SecretReference? ParseReference(string? value) =>
        value is null ? null : new SecretReference(SecretId.Parse(value));

    private sealed record Payload(string ServerName, string ServerDescription, int MaxPlayers, string Difficulty,
        string? ServerPasswordSecretId, string? AdminPasswordSecretId);
}

public sealed class GameConfigurationException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}
