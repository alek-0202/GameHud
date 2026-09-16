using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Definitions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Runtime;

internal sealed record DockerImageInspectionSnapshot(string? Id, IReadOnlyCollection<string>? RepositoryDigests,
    string? OperatingSystem, string? Architecture, string? Variant);

internal static class DockerImagePullStatuses
{
    public const string Dispatched = "dispatched";
    public const string DigestUnavailable = "digest_unavailable";
    public const string AuthenticationUnsupported = "authentication_unsupported";
    public const string DiskFull = "disk_full";
    public const string Rejected = "rejected";
}

internal sealed record DockerImagePullResult(string Status);

internal interface IDockerImageAcquisitionClient
{
    Task<DockerImageInspectionSnapshot?> InspectAsync(string reference, CancellationToken cancellationToken);
    Task<DockerImagePullResult> PullAsync(string reference, string platform, CancellationToken cancellationToken);
}

internal sealed class DockerImageAcquisitionClient(IOptions<DockerOptions> options) : IDockerImageAcquisitionClient
{
    public async Task<DockerImageInspectionSnapshot?> InspectAsync(string reference, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        try
        {
            var response = await client.Images.InspectImageAsync(reference, cancellationToken);
            return new(response.ID, response.RepoDigests?.ToArray(), response.Os, response.Architecture, response.Variant);
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<DockerImagePullResult> PullAsync(string reference, string platform, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var progress = new DockerImageProgressObserver();
        await client.Images.CreateImageAsync(new ImagesCreateParameters
        {
            FromImage = reference,
            Platform = platform
        }, null!, progress, cancellationToken);
        return new(progress.ErrorStatus ?? DockerImagePullStatuses.Dispatched);
    }

    private IDockerClient CreateClient() => string.IsNullOrWhiteSpace(options.Value.Endpoint)
        ? new DockerClientConfiguration().CreateClient()
        : new DockerClientConfiguration(new Uri(options.Value.Endpoint)).CreateClient();
}

// Constant-memory observer. It classifies provider errors without retaining or exposing registry payloads.
internal sealed class DockerImageProgressObserver : IProgress<JSONMessage>
{
    private const int MaximumObservedMessages = 100_000;
    private int observedMessages;
    public int ObservedMessages => observedMessages;
    public bool WasBounded { get; private set; }
    public string? ErrorStatus { get; private set; }

    public void Report(JSONMessage value)
    {
        if (observedMessages < MaximumObservedMessages) observedMessages++;
        else WasBounded = true;
        if (ErrorStatus is not null || value is null) return;
        var message = value.Error?.Message ?? value.ErrorMessage;
        if (string.IsNullOrWhiteSpace(message)) return;
        var normalized = message.ToLowerInvariant();
        ErrorStatus = normalized.Contains("no space left on device", StringComparison.Ordinal)
            ? DockerImagePullStatuses.DiskFull
            : normalized.Contains("manifest unknown", StringComparison.Ordinal)
                || normalized.Contains("not found", StringComparison.Ordinal)
                ? DockerImagePullStatuses.DigestUnavailable
            : normalized.Contains("unauthorized", StringComparison.Ordinal)
                || normalized.Contains("authentication required", StringComparison.Ordinal)
                || normalized.Contains("access denied", StringComparison.Ordinal)
                ? DockerImagePullStatuses.AuthenticationUnsupported
            : DockerImagePullStatuses.Rejected;
    }
}

internal sealed class DockerRuntimeImageAcquisitionAdapter(IDockerImageAcquisitionClient client,
    IOptions<RuntimeImageAcquisitionOptions> options) : IRuntimeImageAcquisitionAdapter
{
    public async Task<RuntimeImageInspectionResult> InspectAsync(TrustedRuntimeImage image,
        CancellationToken cancellationToken)
    {
        if (!IsSupported(image) || !options.Value.IsValid)
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Unprovable);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.InspectTimeoutSeconds));
        try
        {
            var observed = await client.InspectAsync(image.Reference, timeout.Token);
            return observed is null ? RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.NotFound)
                : Verify(image, observed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.ProviderUnavailable);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.ProviderUnavailable);
        }
    }

    public async Task<RuntimeImageAcquisitionResult> AcquireAsync(TrustedRuntimeImage image,
        CancellationToken cancellationToken)
    {
        if (!IsSupported(image) || !options.Value.IsValid)
            return new(RuntimeImageAcquisitionStatuses.Rejected);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var result = await client.PullAsync(image.Reference, Platform(image.Platform!), timeout.Token);
            return new(result.Status switch
            {
                DockerImagePullStatuses.Dispatched => RuntimeImageAcquisitionStatuses.Dispatched,
                DockerImagePullStatuses.DigestUnavailable => RuntimeImageAcquisitionStatuses.DigestUnavailable,
                DockerImagePullStatuses.AuthenticationUnsupported => RuntimeImageAcquisitionStatuses.AuthenticationUnsupported,
                DockerImagePullStatuses.DiskFull => RuntimeImageAcquisitionStatuses.DiskFull,
                _ => RuntimeImageAcquisitionStatuses.Rejected
            });
        }
        catch (OperationCanceledException)
        {
            return new(RuntimeImageAcquisitionStatuses.Interrupted, !cancellationToken.IsCancellationRequested);
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return new(RuntimeImageAcquisitionStatuses.DigestUnavailable);
        }
        catch (DockerApiException exception) when (Contains(exception.ResponseBody, "no space left on device"))
        {
            return new(RuntimeImageAcquisitionStatuses.DiskFull);
        }
        catch (DockerApiException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new(RuntimeImageAcquisitionStatuses.AuthenticationUnsupported);
        }
        catch (DockerApiException exception) when (Contains(exception.ResponseBody, "manifest unknown")
            || Contains(exception.ResponseBody, "not found"))
        {
            return new(RuntimeImageAcquisitionStatuses.DigestUnavailable);
        }
        catch (DockerApiException exception) when (Contains(exception.ResponseBody, "authentication required")
            || Contains(exception.ResponseBody, "unauthorized") || Contains(exception.ResponseBody, "access denied"))
        {
            return new(RuntimeImageAcquisitionStatuses.AuthenticationUnsupported);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            return new(RuntimeImageAcquisitionStatuses.ProviderUnavailable);
        }
    }

    internal static RuntimeImageInspectionResult Verify(TrustedRuntimeImage image, DockerImageInspectionSnapshot observed)
    {
        if (!IsSupported(image) || string.IsNullOrWhiteSpace(observed.Id)
            || string.IsNullOrWhiteSpace(observed.OperatingSystem) || string.IsNullOrWhiteSpace(observed.Architecture))
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Unprovable);
        LocalImageId localId;
        try { localId = new LocalImageId(observed.Id); }
        catch (ArgumentException) { return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Unprovable); }
        var platform = image.Platform!;
        var variant = string.IsNullOrWhiteSpace(observed.Variant) ? null : observed.Variant.Trim().ToLowerInvariant();
        if (!observed.OperatingSystem.Trim().Equals(platform.OperatingSystem, StringComparison.OrdinalIgnoreCase)
            || !observed.Architecture.Trim().Equals(platform.Architecture, StringComparison.OrdinalIgnoreCase)
            || variant != platform.Variant)
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Mismatch);
        var digests = observed.RepositoryDigests?.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        if (digests is null || digests.Count == 0)
            return RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Unprovable);
        var expected = image.Reference;
        var matches = digests.Contains(expected)
            || image.Registry == "docker.io" && digests.Contains($"{image.Repository}@{image.ApprovedDigest!.Value}");
        return matches ? RuntimeImageInspectionResult.Match(localId)
            : RuntimeImageInspectionResult.FromStatus(RuntimeImageInspectionStatuses.Mismatch);
    }

    internal static string Platform(RuntimeImagePlatform platform) => platform.Variant is null
        ? $"{platform.OperatingSystem}/{platform.Architecture}"
        : $"{platform.OperatingSystem}/{platform.Architecture}/{platform.Variant}";

    private static bool IsSupported(TrustedRuntimeImage image) => image.IsPinned && image.RuntimeType == "docker";
    private static bool Contains(string? value, string pattern) =>
        value?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true;
}
