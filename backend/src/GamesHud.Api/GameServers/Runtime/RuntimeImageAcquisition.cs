using GamesHud.Api.GameServers.Definitions;

namespace GamesHud.Api.GameServers.Runtime;

public static class RuntimeImageInspectionStatuses
{
    public const string Matches = "matches";
    public const string NotFound = "not_found";
    public const string Mismatch = "mismatch";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string Unprovable = "unprovable";
}

public sealed record RuntimeImageInspectionResult(string Status, LocalImageId? LocalImageId = null)
{
    public static RuntimeImageInspectionResult Match(LocalImageId imageId) => new(RuntimeImageInspectionStatuses.Matches, imageId);
    public static RuntimeImageInspectionResult FromStatus(string status) => new(status);
}

public static class RuntimeImageAcquisitionStatuses
{
    // Dispatch completion is deliberately not called success. Only a later inspect proves the image.
    public const string Dispatched = "dispatched";
    public const string DigestUnavailable = "digest_unavailable";
    public const string AuthenticationUnsupported = "authentication_unsupported";
    public const string DiskFull = "disk_full";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string Interrupted = "interrupted";
    public const string Rejected = "rejected";
}

public sealed record RuntimeImageAcquisitionResult(string Status, bool TimedOut = false);

public interface IRuntimeImageAcquisitionAdapter
{
    Task<RuntimeImageInspectionResult> InspectAsync(TrustedRuntimeImage image, CancellationToken cancellationToken);
    Task<RuntimeImageAcquisitionResult> AcquireAsync(TrustedRuntimeImage image, CancellationToken cancellationToken);
}

public static class RuntimeImageAcquisitionErrorCodes
{
    public const string InvalidConfiguration = "runtime_image_acquisition_configuration_invalid";
    public const string InvalidIntent = "runtime_image_intent_invalid";
    public const string DigestUnavailable = "runtime_image_digest_unavailable";
    public const string AuthenticationUnsupported = "runtime_image_authentication_unsupported";
    public const string DiskFull = "runtime_image_disk_full";
    public const string Mismatch = "runtime_image_identity_mismatch";
    public const string ProviderUnavailable = "runtime_image_provider_unavailable";
    public const string PullRejected = "runtime_image_pull_rejected";
    public const string OutcomeUnknown = "runtime_image_acquisition_unknown";
    public const string Timeout = "runtime_image_acquisition_timeout";
    public const string Interrupted = "runtime_image_acquisition_interrupted";
}
