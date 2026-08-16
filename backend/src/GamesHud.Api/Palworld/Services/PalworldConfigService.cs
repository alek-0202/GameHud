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
    private const int MaxTextLength = 512;

    private static readonly string[] SettingsFileCandidates =
    [
        SettingsFileName,
        "Pal/Saved/Config/LinuxServer/PalWorldSettings.ini",
        "Pal/Saved/Config/WindowsServer/PalWorldSettings.ini"
    ];

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
        var changedSettings = ApplyRequest(document, request);

        if (changedSettings == 0)
        {
            return new PalworldConfigUpdateResponse(
                "No Palworld settings changed.",
                containerName,
                restart,
                false,
                changedSettings,
                null,
                CreateResponse(document, containerName));
        }

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
            changedSettings,
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

    private static int ApplyRequest(
        PalworldSettingsDocument document,
        PalworldConfigUpdateRequest request)
    {
        var updates = NormalizeUpdates(request);
        var changedSettings = 0;

        foreach (var (definition, serializedValue, comparableValue) in updates)
        {
            if (definition.Type is PalworldSettingType.Password
                && string.IsNullOrEmpty(comparableValue))
            {
                continue;
            }

            var currentComparableValue = document.GetComparableValue(definition);

            if (string.Equals(currentComparableValue, comparableValue, StringComparison.Ordinal))
            {
                continue;
            }

            document.Set(definition.Key, serializedValue);
            changedSettings++;
        }

        return changedSettings;
    }

    private static List<ValidatedSettingUpdate> NormalizeUpdates(PalworldConfigUpdateRequest request)
    {
        var errors = new List<string>();
        var updates = new List<ValidatedSettingUpdate>();

        if (request.Settings is null)
        {
            throw new PalworldConfigValidationException(["Settings are required."]);
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var update in request.Settings)
        {
            if (string.IsNullOrWhiteSpace(update.Key))
            {
                errors.Add("Setting key is required.");
                continue;
            }

            if (!seenKeys.Add(update.Key))
            {
                errors.Add($"{update.Key} was provided more than once.");
                continue;
            }

            if (!PalworldSettingSchema.ByKey.TryGetValue(update.Key, out var definition))
            {
                errors.Add($"{update.Key} is not a supported Palworld setting.");
                continue;
            }

            if (!TryNormalizeValue(definition, update.Value, out var serializedValue, out var comparableValue, out var error))
            {
                errors.Add(error);
                continue;
            }

            updates.Add(new ValidatedSettingUpdate(definition, serializedValue, comparableValue));
        }

        if (errors.Count > 0)
        {
            throw new PalworldConfigValidationException(errors);
        }

        return updates;
    }

    private static bool TryNormalizeValue(
        PalworldSettingDefinition definition,
        string? value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        serializedValue = string.Empty;
        comparableValue = string.Empty;
        error = string.Empty;

        if (definition.Type is PalworldSettingType.Password && string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (value is null)
        {
            error = $"{definition.Key} value is required.";
            return false;
        }

        return definition.Type switch
        {
            PalworldSettingType.Boolean => TryNormalizeBoolean(definition, value, out serializedValue, out comparableValue, out error),
            PalworldSettingType.Integer => TryNormalizeInteger(definition, value, out serializedValue, out comparableValue, out error),
            PalworldSettingType.Decimal => TryNormalizeDecimal(definition, value, out serializedValue, out comparableValue, out error),
            PalworldSettingType.String => TryNormalizeText(definition, value, out serializedValue, out comparableValue, out error),
            PalworldSettingType.Password => TryNormalizeText(definition, value, out serializedValue, out comparableValue, out error),
            PalworldSettingType.Select => TryNormalizeSelect(definition, value, out serializedValue, out comparableValue, out error),
            _ => throw new InvalidOperationException("Unsupported Palworld setting type.")
        };
    }

    private static bool TryNormalizeBoolean(
        PalworldSettingDefinition definition,
        string value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        if (!bool.TryParse(value, out var boolValue))
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be true or false.";
            return false;
        }

        serializedValue = boolValue ? "True" : "False";
        comparableValue = serializedValue;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeInteger(
        PalworldSettingDefinition definition,
        string value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be an integer.";
            return false;
        }

        if (definition.Min is not null && integerValue < definition.Min
            || definition.Max is not null && integerValue > definition.Max)
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be between {definition.Min} and {definition.Max}.";
            return false;
        }

        serializedValue = integerValue.ToString(CultureInfo.InvariantCulture);
        comparableValue = serializedValue;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeDecimal(
        PalworldSettingDefinition definition,
        string value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be a decimal number.";
            return false;
        }

        if (definition.Min is not null && decimalValue < definition.Min
            || definition.Max is not null && decimalValue > definition.Max)
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be between {definition.Min} and {definition.Max}.";
            return false;
        }

        serializedValue = PalworldSettingSchema.FormatDecimal(decimalValue);
        comparableValue = serializedValue;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeText(
        PalworldSettingDefinition definition,
        string value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        if (value.Length > MaxTextLength)
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be {MaxTextLength} characters or fewer.";
            return false;
        }

        if (value.Contains('\r') || value.Contains('\n'))
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} cannot contain line breaks.";
            return false;
        }

        serializedValue = SerializeQuotedString(value.Trim());
        comparableValue = value.Trim();
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeSelect(
        PalworldSettingDefinition definition,
        string value,
        out string serializedValue,
        out string comparableValue,
        out string error)
    {
        var isAllowed = definition.Options.Any(
            option => string.Equals(option.Value, value, StringComparison.Ordinal));

        if (!isAllowed)
        {
            serializedValue = string.Empty;
            comparableValue = string.Empty;
            error = $"{definition.Key} must be one of: {string.Join(", ", definition.Options.Select(option => option.Value))}.";
            return false;
        }

        serializedValue = value;
        comparableValue = value;
        error = string.Empty;
        return true;
    }

    private static PalworldConfigResponse CreateResponse(
        PalworldSettingsDocument document,
        string containerName)
    {
        return new PalworldConfigResponse(
            containerName,
            PalworldSettingSchema.Settings
                .Select(definition => definition.ToResponse(
                    document.GetDisplayValue(definition),
                    document.HasValue(definition.Key)))
                .ToArray());
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

    private sealed record ValidatedSettingUpdate(
        PalworldSettingDefinition Definition,
        string SerializedValue,
        string ComparableValue);

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

        public bool HasValue(string key)
        {
            var rawValue = GetRaw(key);

            return !string.IsNullOrEmpty(rawValue)
                && !string.Equals(rawValue, "\"\"", StringComparison.Ordinal);
        }

        public string? GetDisplayValue(PalworldSettingDefinition definition)
        {
            var rawValue = GetRaw(definition.Key);

            if (rawValue is null)
            {
                return null;
            }

            return definition.Type switch
            {
                PalworldSettingType.Boolean => TryParseBoolean(rawValue, out var boolValue)
                    ? boolValue
                    : rawValue,
                PalworldSettingType.Decimal => TryParseDecimal(rawValue, out var decimalValue)
                    ? PalworldSettingSchema.FormatDecimal(decimalValue)
                    : rawValue,
                PalworldSettingType.Integer => TryParseInteger(rawValue, out var integerValue)
                    ? integerValue.ToString(CultureInfo.InvariantCulture)
                    : rawValue,
                PalworldSettingType.Password => null,
                PalworldSettingType.String => DeserializeQuotedString(rawValue),
                PalworldSettingType.Select => rawValue,
                _ => rawValue
            };
        }

        public string? GetComparableValue(PalworldSettingDefinition definition)
        {
            var rawValue = GetRaw(definition.Key);

            if (rawValue is null)
            {
                return null;
            }

            return definition.Type switch
            {
                PalworldSettingType.Boolean => TryParseBoolean(rawValue, out var boolValue)
                    ? boolValue
                    : rawValue,
                PalworldSettingType.Decimal => TryParseDecimal(rawValue, out var decimalValue)
                    ? PalworldSettingSchema.FormatDecimal(decimalValue)
                    : rawValue,
                PalworldSettingType.Integer => TryParseInteger(rawValue, out var integerValue)
                    ? integerValue.ToString(CultureInfo.InvariantCulture)
                    : rawValue,
                PalworldSettingType.Password => DeserializeQuotedString(rawValue),
                PalworldSettingType.String => DeserializeQuotedString(rawValue),
                PalworldSettingType.Select => rawValue,
                _ => rawValue
            };
        }

        public void Set(string key, string value)
        {
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

        private string? GetRaw(string key)
        {
            return _entries.FirstOrDefault(entry => entry.Key == key)?.Value;
        }

        private static bool TryParseBoolean(string value, out string normalizedValue)
        {
            if (bool.TryParse(value, out var boolValue))
            {
                normalizedValue = boolValue ? "True" : "False";
                return true;
            }

            normalizedValue = string.Empty;
            return false;
        }

        private static bool TryParseDecimal(string value, out decimal result)
        {
            return decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool TryParseInteger(string value, out int result)
        {
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
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
