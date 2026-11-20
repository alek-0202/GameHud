using System.Text.Json;
using System.Text.Json.Serialization;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;

namespace GamesHud.Api.GameServers.Configuration;

public sealed record MinecraftInitialProvisioningConfiguration(
    string ServerName,
    int JavaMemoryMiB,
    int ContainerMemoryMiB,
    int HostPort,
    bool EulaAccepted,
    string PackId,
    string PackVersion,
    string MinecraftVersion,
    string ForgeVersion,
    int JavaVersion,
    int CurseForgeProjectId,
    int CurseForgeFileId,
    string ArtifactFileName,
    string ArtifactUrl,
    string ArtifactSha256,
    long ArtifactSizeBytes,
    string ArtifactRoot);

public sealed class MinecraftProvisioningConfigurationCodec : IGameProvisioningConfigurationCodec
{
    public const string InitialConfigurationKind = "minecraft.lunchtime.initial";
    public const int CurrentSchemaVersion = 1;
    public const int ContainerOverheadMiB = 1024;
    public static readonly IReadOnlyCollection<int> AllowedJavaMemoryMiB = [6144, 8192, 10240, 12288];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public GameId GameId => new("minecraft");
    public string ConfigurationKind => InitialConfigurationKind;
    public int SchemaVersion => CurrentSchemaVersion;

    public ValidatedGameProvisioningConfiguration CreateInitial(
        string serverName,
        Secrets.Models.SecretReference? serverPassword = null,
        Secrets.Models.SecretReference? adminPassword = null) =>
        throw new GameConfigurationException(
            GameConfigurationErrorCodes.Invalid,
            "Minecraft installation choices and explicit EULA acceptance are required.");

    public ValidatedGameProvisioningConfiguration CreateInitial(
        string serverName,
        int javaMemoryMiB,
        int hostPort,
        bool eulaAccepted) => ValidateAndWrap(new(
            serverName,
            javaMemoryMiB,
            checked(javaMemoryMiB + ContainerOverheadMiB),
            hostPort,
            eulaAccepted,
            LunchtimeArtifactManifest.PackId,
            LunchtimeArtifactManifest.PackVersion,
            LunchtimeArtifactManifest.MinecraftVersion,
            LunchtimeArtifactManifest.ForgeVersion,
            LunchtimeArtifactManifest.JavaVersion,
            LunchtimeArtifactManifest.CurseForgeProjectId,
            LunchtimeArtifactManifest.CurseForgeFileId,
            LunchtimeArtifactManifest.FileName,
            LunchtimeArtifactManifest.DownloadUrl,
            LunchtimeArtifactManifest.Sha256,
            LunchtimeArtifactManifest.ArchiveSizeBytes,
            LunchtimeArtifactManifest.ArchiveRoot));

    public ValidatedGameProvisioningConfiguration Deserialize(
        string gameId,
        string kind,
        int schemaVersion,
        string payload)
    {
        if (!string.Equals(gameId, GameId.Value, StringComparison.Ordinal)
            || !string.Equals(kind, ConfigurationKind, StringComparison.Ordinal))
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Game configuration identity is invalid.");
        if (schemaVersion != SchemaVersion)
            throw new GameConfigurationException(GameConfigurationErrorCodes.VersionUnsupported, "Game configuration version is not supported.");

        try
        {
            var value = JsonSerializer.Deserialize<MinecraftInitialProvisioningConfiguration>(payload, JsonOptions)
                ?? throw new JsonException();
            return ValidateAndWrap(value);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException or GameConfigurationException)
        {
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Persisted Minecraft configuration is invalid.");
        }
    }

    private ValidatedGameProvisioningConfiguration ValidateAndWrap(MinecraftInitialProvisioningConfiguration value)
    {
        if (string.IsNullOrWhiteSpace(value.ServerName)
            || value.ServerName.Length > 100
            || value.ServerName.Any(char.IsControl)
            || !AllowedJavaMemoryMiB.Contains(value.JavaMemoryMiB)
            || value.ContainerMemoryMiB != checked(value.JavaMemoryMiB + ContainerOverheadMiB)
            || value.HostPort is < 1024 or > 65535
            || !value.EulaAccepted
            || !HasApprovedManifest(value))
            throw new GameConfigurationException(GameConfigurationErrorCodes.Invalid, "Minecraft installation configuration is invalid.");

        var normalized = value with { ServerName = value.ServerName.Trim() };
        var payload = JsonSerializer.Serialize(normalized, JsonOptions);
        return new(GameId, ConfigurationKind, SchemaVersion, payload, normalized);
    }

    private static bool HasApprovedManifest(MinecraftInitialProvisioningConfiguration value) =>
        value.PackId == LunchtimeArtifactManifest.PackId
        && value.PackVersion == LunchtimeArtifactManifest.PackVersion
        && value.MinecraftVersion == LunchtimeArtifactManifest.MinecraftVersion
        && value.ForgeVersion == LunchtimeArtifactManifest.ForgeVersion
        && value.JavaVersion == LunchtimeArtifactManifest.JavaVersion
        && value.CurseForgeProjectId == LunchtimeArtifactManifest.CurseForgeProjectId
        && value.CurseForgeFileId == LunchtimeArtifactManifest.CurseForgeFileId
        && value.ArtifactFileName == LunchtimeArtifactManifest.FileName
        && value.ArtifactUrl == LunchtimeArtifactManifest.DownloadUrl
        && value.ArtifactSha256 == LunchtimeArtifactManifest.Sha256
        && value.ArtifactSizeBytes == LunchtimeArtifactManifest.ArchiveSizeBytes
        && value.ArtifactRoot == LunchtimeArtifactManifest.ArchiveRoot;
}
