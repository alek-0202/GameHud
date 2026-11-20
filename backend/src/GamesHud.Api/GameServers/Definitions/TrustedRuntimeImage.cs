namespace GamesHud.Api.GameServers.Definitions;

public sealed record TrustedRuntimeImage
{
    public TrustedRuntimeImage(string runtimeType, string repository, string tag, string source)
    {
        RuntimeType = Normalize(runtimeType, nameof(runtimeType));
        Repository = Normalize(repository, nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        Tag = tag.Trim();
        Source = Normalize(source, nameof(source));
    }

    public TrustedRuntimeImage(string runtimeType, string registry, string repository,
        ApprovedImageDigest approvedDigest, RuntimeImagePlatform platform, string source)
    {
        RuntimeType = Normalize(runtimeType, nameof(runtimeType));
        Registry = ImageIdentityValidation.Registry(registry);
        Repository = ImageIdentityValidation.Repository(repository);
        ApprovedDigest = approvedDigest ?? throw new ArgumentNullException(nameof(approvedDigest));
        Platform = platform ?? throw new ArgumentNullException(nameof(platform));
        Source = Normalize(source, nameof(source));
        if (RuntimeType != "docker" || Source.Length > 120)
            throw new ArgumentException("Pinned runtime image metadata is unsupported.");
        Tag = string.Empty;
    }

    public string RuntimeType { get; }
    public string Repository { get; }
    public string Tag { get; }
    public string Source { get; }
    public string? Registry { get; }
    public ApprovedImageDigest? ApprovedDigest { get; }
    public RuntimeImagePlatform? Platform { get; }
    public bool IsPinned => ApprovedDigest is not null && Platform is not null && Registry is not null;
    public string Reference => IsPinned ? $"{Registry}/{Repository}@{ApprovedDigest!.Value}" : $"{Repository}:{Tag}";

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().ToLowerInvariant();
    }
}
