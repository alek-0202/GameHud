using System.Globalization;
using System.Text;

namespace GamesHud.Api.Palworld.ManagedConfiguration;

public interface IPalworldManagedConfigurationSerializer
{
    string Serialize(PalworldManagedConfiguration configuration);
    bool TryParse(string content, out PalworldManagedConfiguration? configuration);
}

public sealed class PalworldManagedConfigurationSerializer : IPalworldManagedConfigurationSerializer
{
    private const string Header = "[/Script/Pal.PalGameWorldSettings]";
    private static readonly string[] RequiredKeys =
    [
        "ServerName", "ServerDescription", "ServerPlayerMaxNum", "Difficulty", "ServerPassword", "AdminPassword"
    ];

    public string Serialize(PalworldManagedConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateString(configuration.ServerName, allowEmpty: false);
        ValidateString(configuration.ServerDescription, allowEmpty: true);
        ValidateString(configuration.ServerPassword, allowEmpty: true);
        ValidateString(configuration.AdminPassword, allowEmpty: true);
        if (configuration.MaxPlayers is < 1 or > 32 || configuration.Difficulty is not "None" and not "Normal" and not "Hard")
            throw Invalid();

        return string.Create(CultureInfo.InvariantCulture,
            $"{Header}\nOptionSettings=(ServerName=\"{Escape(configuration.ServerName)}\",ServerDescription=\"{Escape(configuration.ServerDescription)}\",ServerPlayerMaxNum={configuration.MaxPlayers},Difficulty={ToPalworldDifficulty(configuration.Difficulty)},ServerPassword=\"{Escape(configuration.ServerPassword)}\",AdminPassword=\"{Escape(configuration.AdminPassword)}\")\n");
    }

    public bool TryParse(string content, out PalworldManagedConfiguration? configuration)
    {
        configuration = null;
        if (content is null) return false;
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        var lines = normalized.Split('\n');
        if (lines.Length != 2 || lines[0] != Header) return false;

        const string prefix = "OptionSettings=(";
        if (!lines[1].StartsWith(prefix, StringComparison.Ordinal) || !lines[1].EndsWith(')')) return false;
        var parser = new PropertyParser(lines[1].AsSpan(prefix.Length, lines[1].Length - prefix.Length - 1));
        if (!parser.TryParse(out var values) || values.Count != RequiredKeys.Length
            || RequiredKeys.Any(key => !values.ContainsKey(key))) return false;
        if (!TryUnquoted(values, "ServerPlayerMaxNum", out var playersText)
            || !int.TryParse(playersText, NumberStyles.None, CultureInfo.InvariantCulture, out var players)
            || players is < 1 or > 32
            || !TryUnquoted(values, "Difficulty", out var difficultyText)
            || !TryFromPalworldDifficulty(difficultyText, out var difficulty)
            || !TryQuoted(values, "ServerName", out var serverName)
            || !TryQuoted(values, "ServerDescription", out var description)
            || !TryQuoted(values, "ServerPassword", out var serverPassword)
            || !TryQuoted(values, "AdminPassword", out var adminPassword)
            || string.IsNullOrEmpty(serverName)) return false;

        configuration = new(serverName, description, players, difficulty!, serverPassword, adminPassword);
        return true;
    }

    private static string ToPalworldDifficulty(string value) => value == "Hard" ? "Difficult" : value;

    private static bool TryFromPalworldDifficulty(string value, out string? result)
    {
        result = value switch { "None" => "None", "Normal" => "Normal", "Difficult" => "Hard", _ => null };
        return result is not null;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void ValidateString(string? value, bool allowEmpty)
    {
        if (value is null || !allowEmpty && value.Length == 0 || value.Any(char.IsControl)) throw Invalid();
    }

    private static PalworldManagedConfigurationException Invalid() => new(
        PalworldManagedConfigurationErrorCodes.IntentInvalid,
        "Managed Palworld configuration contains a value that cannot be represented safely.");

    private static bool TryUnquoted(Dictionary<string, ParsedValue> values, string key, out string value)
    {
        var parsed = values[key];
        value = parsed.Value;
        return !parsed.Quoted && value.Length > 0;
    }

    private static bool TryQuoted(Dictionary<string, ParsedValue> values, string key, out string value)
    {
        var parsed = values[key];
        value = parsed.Value;
        return parsed.Quoted && !value.Any(char.IsControl);
    }

    private sealed record ParsedValue(bool Quoted, string Value);

    private ref struct PropertyParser
    {
        private readonly ReadOnlySpan<char> _source;
        private int _position;

        public PropertyParser(ReadOnlySpan<char> source)
        {
            _source = source;
            _position = 0;
        }

        public bool TryParse(out Dictionary<string, ParsedValue> values)
        {
            values = new(StringComparer.Ordinal);
            while (_position < _source.Length)
            {
                SkipSpaces();
                var keyStart = _position;
                while (_position < _source.Length && char.IsAsciiLetterOrDigit(_source[_position])) _position++;
                if (_position == keyStart) return false;
                var key = _source[keyStart.._position].ToString();
                SkipSpaces();
                if (!Take('=')) return false;
                SkipSpaces();
                if (!TryReadValue(out var value) || !values.TryAdd(key, value)) return false;
                SkipSpaces();
                if (_position == _source.Length) return true;
                if (!Take(',')) return false;
            }
            return false;
        }

        private bool TryReadValue(out ParsedValue value)
        {
            if (Take('"'))
            {
                var builder = new StringBuilder();
                while (_position < _source.Length)
                {
                    var current = _source[_position++];
                    if (current == '"') { value = new(true, builder.ToString()); return true; }
                    if (current == '\\')
                    {
                        if (_position >= _source.Length || _source[_position] is not ('\\' or '"')) break;
                        current = _source[_position++];
                    }
                    if (char.IsControl(current)) break;
                    builder.Append(current);
                }
                value = new(false, string.Empty);
                return false;
            }

            var start = _position;
            while (_position < _source.Length && _source[_position] != ',')
            {
                if (char.IsControl(_source[_position]) || _source[_position] is '(' or ')' or '"' or '\\')
                {
                    value = new(false, string.Empty);
                    return false;
                }
                _position++;
            }
            var text = _source[start.._position].Trim().ToString();
            value = new(false, text);
            return text.Length > 0;
        }

        private void SkipSpaces() { while (_position < _source.Length && _source[_position] == ' ') _position++; }
        private bool Take(char expected)
        {
            if (_position >= _source.Length || _source[_position] != expected) return false;
            _position++;
            return true;
        }
    }
}
