namespace GamesHud.Api.GameServers.Definitions;

public sealed record TrustedRuntimeImage
{
    public TrustedRuntimeImage(string runtimeType, string repository, string tag, string source)
    {
        RuntimeType = Normalize(runtimeType, nameof(runtimeType));
        Repository = Normalize(repository, nameof(repository));
        Tag = Normalize(tag, nameof(tag));
        Source = Normalize(source, nameof(source));
    }

    public string RuntimeType { get; }
    public string Repository { get; }
    public string Tag { get; }
    public string Source { get; }
    public string Reference => $"{Repository}:{Tag}";

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().ToLowerInvariant();
    }
}
