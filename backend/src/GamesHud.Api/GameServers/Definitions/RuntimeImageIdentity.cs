using System.Text.RegularExpressions;

namespace GamesHud.Api.GameServers.Definitions;

public sealed record ApprovedImageDigest
{
    public ApprovedImageDigest(string value) => Value = ImageIdentityValidation.Sha256(value);
    public string Value { get; }
}

public sealed record LocalImageId
{
    public LocalImageId(string value) => Value = ImageIdentityValidation.Sha256(value);
    public string Value { get; }
}

public sealed record RuntimeImagePlatform
{
    public RuntimeImagePlatform(string operatingSystem, string architecture, string? variant = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatingSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        OperatingSystem = operatingSystem.Trim().ToLowerInvariant();
        Architecture = architecture.Trim().ToLowerInvariant();
        Variant = variant?.Trim().ToLowerInvariant();
        if (OperatingSystem is not "linux" and not "windows"
            || Architecture is not "amd64" and not "arm64"
            || Variant is not null && (Architecture != "arm64" || !Regex.IsMatch(Variant, "^v[1-9][0-9]?$")))
            throw new ArgumentException("Runtime image platform is unsupported.");
    }

    public string OperatingSystem { get; }
    public string Architecture { get; }
    public string? Variant { get; }
}

internal static class ImageIdentityValidation
{
    internal static string Sha256(string value)
    {
        if (value is null || !Regex.IsMatch(value, "^sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Image identity must be a complete sha256 digest.");
        return value.ToLowerInvariant();
    }

    internal static string Registry(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 253 || !Regex.IsMatch(normalized,
            "^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?::[0-9]{1,5})?$", RegexOptions.CultureInvariant)
            || normalized.Contains("..") || normalized.Contains(".-") || normalized.Contains("-."))
            throw new ArgumentException("Image registry is invalid.");
        if (normalized.Contains(':') && (!int.TryParse(normalized.Split(':')[1], out var port) || port is < 1 or > 65535))
            throw new ArgumentException("Image registry port is invalid.");
        return normalized;
    }

    internal static string Repository(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 255 || !Regex.IsMatch(normalized,
            "^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*$",
            RegexOptions.CultureInvariant))
            throw new ArgumentException("Image repository is invalid.");
        return normalized;
    }
}
