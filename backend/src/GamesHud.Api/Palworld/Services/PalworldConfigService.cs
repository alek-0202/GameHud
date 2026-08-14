using System.Globalization;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Contracts;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Palworld.Services;

public sealed class PalworldConfigService : IPalworldConfigService
{
    private const string SettingsFileName = "PalWorldSettings.ini";
    private const string OptionSettingsMarker = "OptionSettings=(";
    private const int LifecycleTimeoutSeconds = 10;

    private static readonly string[] SettingsFileCandidates =
    [
        SettingsFileName,
        "Pal/Saved/Config/LinuxServer/PalWorldSettings.ini",
        "Pal/Saved/Config/WindowsServer/PalWorldSettings.ini"
    ];

    private static readonly HashSet<string> SupportedKeys = new(StringComparer.Ordinal)
    {
        PalworldConfigKey.ServerName,
        PalworldConfigKey.ServerPassword,
        PalworldConfigKey.ExpRate,
        PalworldConfigKey.PlayerDamageRateAttack,
        PalworldConfigKey.PalCaptureRate,
        PalworldConfigKey.PlayerStomachDecreaceRate,
        PalworldConfigKey.PlayerStaminaDecreaceRate,
        PalworldConfigKey.WorkSpeedRate,
        PalworldConfigKey.CollectionDropRate,
        PalworldConfigKey.EnemyDropItemRate,
        PalworldConfigKey.PalEggDefaultHatchingTime,
        PalworldConfigKey.DeathPenalty,
        PalworldConfigKey.GuildPlayerMaxNum,
        PalworldConfigKey.BaseCampMaxNum,
        PalworldConfigKey.BaseCampWorkerMaxNum
    };

    private static readonly HashSet<string> AllowedDeathPenaltyValues = new(StringComparer.Ordinal)
    {
        "None",
        "Item",
        "ItemAndEquipment",
        "All"
    };

    private readonly IOptions<PalworldOptions> _options;
    private readonly IPalworldConfigFileSystem _fileSystem;
    private readonly IContainerService _containerService;
    private readonly ILogger<PalworldConfigService> _logger;

    public PalworldConfigService(
        IOptions<PalworldOptions> options,
        IPalworldConfigFileSystem fileSystem,
        IContainerService containerService,
        ILogger<PalworldConfigService> logger)
    {
        _options = options;
        _fileSystem = fileSystem;
        _containerService = containerService;
        _logger = logger;
    }

    public async Task<PalworldConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
    {
        var settingsFile = ResolveSettingsFile();
        var currentText = await _fileSystem.ReadAllTextAsync(settingsFile, cancellationToken);
        var document = PalworldSettingsDocument.Parse(currentText);

        return CreateResponse(document, ResolveContainerName());
    }

    public async Task<PalworldConfigUpdateResponse> UpdateConfigAsync(
        PalworldConfigUpdateRequest request,
        bool restart,
        CancellationToken cancellationToken)
    {
        var containerName = ResolveContainerName();
        var settingsFile = ResolveSettingsFile();
        var currentText = await _fileSystem.ReadAllTextAsync(settingsFile, cancellationToken);
        var document = PalworldSettingsDocument.Parse(currentText);

        ApplyRequest(document, request);

        if (restart)
        {
            await StopConfiguredContainerAsync(containerName, cancellationToken);
        }

        var backupFile = await WriteSafelyAsync(
            settingsFile,
            document.ToText(),
            cancellationToken);

        if (restart)
        {
            await StartConfiguredContainerAsync(containerName, cancellationToken);
        }

        return new PalworldConfigUpdateResponse(
            restart
                ? "Palworld configuration saved and configured container start was requested."
                : "Palworld configuration saved.",
            containerName,
            restart,
            restart,
            Path.GetFileName(backupFile),
            CreateResponse(document, containerName));
    }

    private string ResolveManagedPath()
    {
        var managedPath = _options.Value.ManagedPath;

        if (string.IsNullOrWhiteSpace(managedPath))
        {
            throw new PalworldConfigException("Palworld managed path is not configured.");
        }

        var fullPath = Path.GetFullPath(managedPath);

        if (!_fileSystem.DirectoryExists(fullPath))
        {
            throw new PalworldConfigNotFoundException("Configured Palworld managed path was not found.");
        }

        return fullPath;
    }

    private string ResolveContainerName()
    {
        var containerName = _options.Value.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new PalworldConfigException("Palworld container name is not configured.");
        }

        return containerName.Trim();
    }

    private string ResolveSettingsFile()
    {
        var managedPath = ResolveManagedPath();

        foreach (var candidate in SettingsFileCandidates)
        {
            var candidatePath = Path.GetFullPath(
                Path.Combine(new[] { managedPath }.Concat(candidate.Split('/')).ToArray()));

            if (IsInsideManagedPath(managedPath, candidatePath)
                && File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        var discoveredSettings = _fileSystem
            .EnumerateFiles(managedPath, SettingsFileName, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .FirstOrDefault(path => IsInsideManagedPath(managedPath, path));

        if (discoveredSettings is null)
        {
            throw new PalworldConfigNotFoundException("PalWorldSettings.ini was not found in the configured Palworld managed path.");
        }

        return discoveredSettings;
    }

    private async Task<string> WriteSafelyAsync(
        string settingsFile,
        string updatedText,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(settingsFile)
            ?? throw new PalworldConfigWriteException(
                "Palworld configuration path is invalid.",
                new InvalidOperationException("Settings file has no parent directory."));
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var backupFile = Path.Combine(directory, $"{SettingsFileName}.{timestamp}.{Guid.NewGuid():N}.bak");
        var temporaryFile = Path.Combine(directory, $".{SettingsFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            _fileSystem.Copy(settingsFile, backupFile, overwrite: false);
            await _fileSystem.WriteAllTextAsync(temporaryFile, updatedText, cancellationToken);
            _fileSystem.Move(temporaryFile, settingsFile, overwrite: true);

            return backupFile;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _fileSystem.DeleteIfExists(temporaryFile);

            try
            {
                if (File.Exists(backupFile))
                {
                    _fileSystem.Copy(backupFile, settingsFile, overwrite: true);
                }
            }
            catch (Exception restoreException) when (restoreException is not OperationCanceledException)
            {
                _logger.LogError(
                    restoreException,
                    "Unable to restore Palworld configuration backup after write failure.");
            }

            throw new PalworldConfigWriteException(
                "Palworld configuration could not be saved. The previous file was restored when possible.",
                exception);
        }
    }

    private async Task StopConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await _containerService.StopContainerAsync(
            containerName,
            LifecycleTimeoutSeconds,
            cancellationToken);

        if (result is null)
        {
            throw new PalworldContainerLifecycleException("Configured Palworld container was not found.");
        }

        if (!result.Success)
        {
            throw new PalworldContainerLifecycleException("Configured Palworld container could not be stopped.");
        }
    }

    private async Task StartConfiguredContainerAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await _containerService.StartContainerAsync(containerName, cancellationToken);

        if (result is null)
        {
            throw new PalworldContainerLifecycleException("Configured Palworld container was not found.");
        }

        if (!result.Success)
        {
            throw new PalworldContainerLifecycleException("Configured Palworld container could not be started.");
        }
    }

    private static void ApplyRequest(
        PalworldSettingsDocument document,
        PalworldConfigUpdateRequest request)
    {
        var errors = Validate(request);

        if (errors.Count > 0)
        {
            throw new PalworldConfigValidationException(errors);
        }

        SetStringIfProvided(document, PalworldConfigKey.ServerName, request.ServerName);

        if (!string.IsNullOrEmpty(request.ServerPassword))
        {
            document.Set(PalworldConfigKey.ServerPassword, SerializeQuotedString(request.ServerPassword));
        }

        SetDecimalIfProvided(document, PalworldConfigKey.ExpRate, request.ExpRate);
        SetDecimalIfProvided(document, PalworldConfigKey.PlayerDamageRateAttack, request.PlayerDamageRateAttack);
        SetDecimalIfProvided(document, PalworldConfigKey.PalCaptureRate, request.PalCaptureRate);
        SetDecimalIfProvided(document, PalworldConfigKey.PlayerStomachDecreaceRate, request.PlayerStomachDecreaceRate);
        SetDecimalIfProvided(document, PalworldConfigKey.PlayerStaminaDecreaceRate, request.PlayerStaminaDecreaceRate);
        SetDecimalIfProvided(document, PalworldConfigKey.WorkSpeedRate, request.WorkSpeedRate);
        SetDecimalIfProvided(document, PalworldConfigKey.CollectionDropRate, request.CollectionDropRate);
        SetDecimalIfProvided(document, PalworldConfigKey.EnemyDropItemRate, request.EnemyDropItemRate);
        SetDecimalIfProvided(document, PalworldConfigKey.PalEggDefaultHatchingTime, request.PalEggDefaultHatchingTime);
        SetEnumIfProvided(document, PalworldConfigKey.DeathPenalty, request.DeathPenalty);
        SetIntegerIfProvided(document, PalworldConfigKey.GuildPlayerMaxNum, request.GuildPlayerMaxNum);
        SetIntegerIfProvided(document, PalworldConfigKey.BaseCampMaxNum, request.BaseCampMaxNum);
        SetIntegerIfProvided(document, PalworldConfigKey.BaseCampWorkerMaxNum, request.BaseCampWorkerMaxNum);
    }

    private static List<string> Validate(PalworldConfigUpdateRequest request)
    {
        var errors = new List<string>();

        ValidateText(errors, nameof(request.ServerName), request.ServerName, requiredWhenProvided: true);
        ValidateText(errors, nameof(request.ServerPassword), request.ServerPassword, requiredWhenProvided: false);
        ValidateRate(errors, nameof(request.ExpRate), request.ExpRate);
        ValidateRate(errors, nameof(request.PlayerDamageRateAttack), request.PlayerDamageRateAttack);
        ValidateRate(errors, nameof(request.PalCaptureRate), request.PalCaptureRate);
        ValidateRate(errors, nameof(request.PlayerStomachDecreaceRate), request.PlayerStomachDecreaceRate);
        ValidateRate(errors, nameof(request.PlayerStaminaDecreaceRate), request.PlayerStaminaDecreaceRate);
        ValidateRate(errors, nameof(request.WorkSpeedRate), request.WorkSpeedRate);
        ValidateRate(errors, nameof(request.CollectionDropRate), request.CollectionDropRate);
        ValidateRate(errors, nameof(request.EnemyDropItemRate), request.EnemyDropItemRate);
        ValidateRate(errors, nameof(request.PalEggDefaultHatchingTime), request.PalEggDefaultHatchingTime);
        ValidatePositiveInteger(errors, nameof(request.GuildPlayerMaxNum), request.GuildPlayerMaxNum);
        ValidatePositiveInteger(errors, nameof(request.BaseCampMaxNum), request.BaseCampMaxNum);
        ValidatePositiveInteger(errors, nameof(request.BaseCampWorkerMaxNum), request.BaseCampWorkerMaxNum);

        if (request.DeathPenalty is { } deathPenalty
            && !AllowedDeathPenaltyValues.Contains(deathPenalty))
        {
            errors.Add("DeathPenalty must be one of: None, Item, ItemAndEquipment, All.");
        }

        return errors;
    }

    private static void ValidateText(
        List<string> errors,
        string fieldName,
        string? value,
        bool requiredWhenProvided)
    {
        if (value is null)
        {
            return;
        }

        if (requiredWhenProvided && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} cannot be empty.");
            return;
        }

        if (value.Length > 128)
        {
            errors.Add($"{fieldName} must be 128 characters or fewer.");
        }

        if (value.Contains('\r') || value.Contains('\n'))
        {
            errors.Add($"{fieldName} cannot contain line breaks.");
        }
    }

    private static void ValidateRate(List<string> errors, string fieldName, decimal? value)
    {
        if (value is null)
        {
            return;
        }

        if (value < 0 || value > 100)
        {
            errors.Add($"{fieldName} must be between 0 and 100.");
        }
    }

    private static void ValidatePositiveInteger(List<string> errors, string fieldName, int? value)
    {
        if (value is null)
        {
            return;
        }

        if (value < 1 || value > 10000)
        {
            errors.Add($"{fieldName} must be between 1 and 10000.");
        }
    }

    private static void SetStringIfProvided(
        PalworldSettingsDocument document,
        string key,
        string? value)
    {
        if (value is not null)
        {
            document.Set(key, SerializeQuotedString(value.Trim()));
        }
    }

    private static void SetEnumIfProvided(
        PalworldSettingsDocument document,
        string key,
        string? value)
    {
        if (value is not null)
        {
            document.Set(key, value);
        }
    }

    private static void SetDecimalIfProvided(
        PalworldSettingsDocument document,
        string key,
        decimal? value)
    {
        if (value is not null)
        {
            document.Set(key, value.Value.ToString("0.######", CultureInfo.InvariantCulture));
        }
    }

    private static void SetIntegerIfProvided(
        PalworldSettingsDocument document,
        string key,
        int? value)
    {
        if (value is not null)
        {
            document.Set(key, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static PalworldConfigResponse CreateResponse(
        PalworldSettingsDocument document,
        string containerName)
    {
        return new PalworldConfigResponse(
            containerName,
            document.GetString(PalworldConfigKey.ServerName),
            !string.IsNullOrEmpty(document.GetString(PalworldConfigKey.ServerPassword)),
            document.GetDecimal(PalworldConfigKey.ExpRate),
            document.GetDecimal(PalworldConfigKey.PlayerDamageRateAttack),
            document.GetDecimal(PalworldConfigKey.PalCaptureRate),
            document.GetDecimal(PalworldConfigKey.PlayerStomachDecreaceRate),
            document.GetDecimal(PalworldConfigKey.PlayerStaminaDecreaceRate),
            document.GetDecimal(PalworldConfigKey.WorkSpeedRate),
            document.GetDecimal(PalworldConfigKey.CollectionDropRate),
            document.GetDecimal(PalworldConfigKey.EnemyDropItemRate),
            document.GetDecimal(PalworldConfigKey.PalEggDefaultHatchingTime),
            document.GetRaw(PalworldConfigKey.DeathPenalty),
            document.GetInteger(PalworldConfigKey.GuildPlayerMaxNum),
            document.GetInteger(PalworldConfigKey.BaseCampMaxNum),
            document.GetInteger(PalworldConfigKey.BaseCampWorkerMaxNum));
    }

    private static string SerializeQuotedString(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string DeserializeQuotedString(string value)
    {
        var trimmedValue = value.Trim();

        if (trimmedValue.Length >= 2
            && trimmedValue[0] == '"'
            && trimmedValue[^1] == '"')
        {
            return trimmedValue[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return trimmedValue;
    }

    private static bool IsInsideManagedPath(string managedPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedPath));
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static class PalworldConfigKey
    {
        public const string ServerName = nameof(ServerName);
        public const string ServerPassword = nameof(ServerPassword);
        public const string ExpRate = nameof(ExpRate);
        public const string PlayerDamageRateAttack = nameof(PlayerDamageRateAttack);
        public const string PalCaptureRate = nameof(PalCaptureRate);
        public const string PlayerStomachDecreaceRate = nameof(PlayerStomachDecreaceRate);
        public const string PlayerStaminaDecreaceRate = nameof(PlayerStaminaDecreaceRate);
        public const string WorkSpeedRate = nameof(WorkSpeedRate);
        public const string CollectionDropRate = nameof(CollectionDropRate);
        public const string EnemyDropItemRate = nameof(EnemyDropItemRate);
        public const string PalEggDefaultHatchingTime = nameof(PalEggDefaultHatchingTime);
        public const string DeathPenalty = nameof(DeathPenalty);
        public const string GuildPlayerMaxNum = nameof(GuildPlayerMaxNum);
        public const string BaseCampMaxNum = nameof(BaseCampMaxNum);
        public const string BaseCampWorkerMaxNum = nameof(BaseCampWorkerMaxNum);
    }

    private sealed class PalworldSettingsDocument
    {
        private readonly string _prefix;
        private readonly string _suffix;
        private readonly List<PalworldSettingEntry> _entries;

        private PalworldSettingsDocument(
            string prefix,
            string suffix,
            List<PalworldSettingEntry> entries)
        {
            _prefix = prefix;
            _suffix = suffix;
            _entries = entries;
        }

        public static PalworldSettingsDocument Parse(string text)
        {
            var markerIndex = text.IndexOf(OptionSettingsMarker, StringComparison.Ordinal);

            if (markerIndex < 0)
            {
                throw new PalworldConfigException("Palworld OptionSettings block was not found.");
            }

            var bodyStart = markerIndex + OptionSettingsMarker.Length;
            var bodyEnd = FindOptionSettingsEnd(text, bodyStart);

            if (bodyEnd < 0)
            {
                throw new PalworldConfigException("Palworld OptionSettings block is malformed.");
            }

            var prefix = text[..bodyStart];
            var body = text[bodyStart..bodyEnd];
            var suffix = text[bodyEnd..];
            var entries = SplitEntries(body)
                .Select(PalworldSettingEntry.Parse)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Raw))
                .ToList();

            return new PalworldSettingsDocument(prefix, suffix, entries);
        }

        public string? GetRaw(string key)
        {
            return _entries.FirstOrDefault(entry => entry.Key == key)?.Value;
        }

        public string? GetString(string key)
        {
            var value = GetRaw(key);

            return value is null ? null : DeserializeQuotedString(value);
        }

        public decimal? GetDecimal(string key)
        {
            var value = GetRaw(key);

            if (value is null)
            {
                return null;
            }

            return decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var result)
                    ? result
                    : null;
        }

        public int? GetInteger(string key)
        {
            var value = GetRaw(key);

            if (value is null)
            {
                return null;
            }

            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result)
                    ? result
                    : null;
        }

        public void Set(string key, string value)
        {
            if (!SupportedKeys.Contains(key))
            {
                throw new InvalidOperationException("Only supported Palworld settings can be updated.");
            }

            var existing = _entries.FirstOrDefault(entry => entry.Key == key);

            if (existing is not null)
            {
                existing.Value = value;
                return;
            }

            _entries.Add(new PalworldSettingEntry($"{key}={value}", key, value));
        }

        public string ToText()
        {
            return _prefix
                + string.Join(",", _entries.Select(entry => entry.ToText()))
                + _suffix;
        }

        private static int FindOptionSettingsEnd(string text, int bodyStart)
        {
            var isInQuotes = false;
            var isEscaped = false;
            var depth = 1;

            for (var index = bodyStart; index < text.Length; index++)
            {
                var character = text[index];

                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (character == '\\' && isInQuotes)
                {
                    isEscaped = true;
                    continue;
                }

                if (character == '"')
                {
                    isInQuotes = !isInQuotes;
                    continue;
                }

                if (isInQuotes)
                {
                    continue;
                }

                if (character == '(')
                {
                    depth++;
                    continue;
                }

                if (character == ')')
                {
                    depth--;

                    if (depth == 0)
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        private static IEnumerable<string> SplitEntries(string body)
        {
            var start = 0;
            var isInQuotes = false;
            var isEscaped = false;

            for (var index = 0; index < body.Length; index++)
            {
                var character = body[index];

                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (character == '\\' && isInQuotes)
                {
                    isEscaped = true;
                    continue;
                }

                if (character == '"')
                {
                    isInQuotes = !isInQuotes;
                    continue;
                }

                if (character == ',' && !isInQuotes)
                {
                    yield return body[start..index].Trim();
                    start = index + 1;
                }
            }

            yield return body[start..].Trim();
        }
    }

    private sealed class PalworldSettingEntry
    {
        public PalworldSettingEntry(string raw, string? key, string? value)
        {
            Raw = raw;
            Key = key;
            Value = value;
        }

        public string Raw { get; }

        public string? Key { get; }

        public string? Value { get; set; }

        public static PalworldSettingEntry Parse(string raw)
        {
            var separatorIndex = raw.IndexOf('=', StringComparison.Ordinal);

            if (separatorIndex < 1)
            {
                return new PalworldSettingEntry(raw, null, null);
            }

            var key = raw[..separatorIndex].Trim();
            var value = raw[(separatorIndex + 1)..].Trim();

            return new PalworldSettingEntry(raw, key, value);
        }

        public string ToText()
        {
            if (Key is null || Value is null)
            {
                return Raw;
            }

            return $"{Key}={Value}";
        }
    }
}
