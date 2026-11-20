using GamesHud.Api.GameServers.Definitions;

namespace GamesHud.Api.GameServers.Provisioning;

public sealed record ProvisioningPipelineDefinition(string Version, IReadOnlyCollection<ProvisioningStepDefinition> Steps)
{
    public bool Matches(ProvisioningOperationSnapshot operation) => operation.Steps.Count == Steps.Count
        && Steps.All(expected => operation.Steps.Any(actual => actual.StepId == expected.Id
            && actual.Sequence == expected.Sequence && actual.RetryClassification == expected.RetryClassification
            && actual.SideEffectClassification == expected.SideEffectClassification && actual.MaxAttempts == expected.MaxAttempts));
}

public static class ProvisioningPipelines
{
    public const string ImageAcquisitionVersion = "gh15-v2";
    public const string MinecraftDumbModeVersion = "gh-mc01-v1";
    public static string DefaultVersion => ProvisioningPipeline.Version;
    public static ProvisioningPipelineDefinition Legacy { get; } = new(ProvisioningPipeline.Version, ProvisioningPipeline.Steps);
    public static ProvisioningPipelineDefinition ImageAcquisition { get; } = new(ImageAcquisitionVersion,
        Array.AsReadOnly(ProvisioningPipeline.Steps.Select(step => step.Sequence >= 6 ? step with { Sequence = step.Sequence + 1 } : step)
            .Select(step => step.SideEffectClassification == ProvisioningSideEffectClassifications.Mutation ? step with { MaxAttempts = 3 } : step)
            .Append(new(ProvisioningStepIds.AcquireImage, 6, ProvisioningRetryClassifications.RequiresInspection,
                ProvisioningSideEffectClassifications.Mutation, 3)).OrderBy(step => step.Sequence).ToArray()));
    public static ProvisioningPipelineDefinition MinecraftDumbMode { get; } = new(MinecraftDumbModeVersion,
    [
        new(ProvisioningStepIds.PrepareStorage, 1, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.AcquireImage, 2, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.AcquireGameArtifact, 3, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.ConfigureGame, 4, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.CreateRuntime, 5, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.StartRuntime, 6, ProvisioningRetryClassifications.RequiresInspection, ProvisioningSideEffectClassifications.Mutation, 3),
        new(ProvisioningStepIds.VerifyHealth, 7, ProvisioningRetryClassifications.SafeToRetry, ProvisioningSideEffectClassifications.ReadOnly, 2),
        new(ProvisioningStepIds.Complete, 8, ProvisioningRetryClassifications.SafeToRetry, ProvisioningSideEffectClassifications.ReadOnly, 1)
    ]);

    public static ProvisioningPipelineDefinition? Find(string version) => version switch
    {
        ProvisioningPipeline.Version => Legacy,
        ImageAcquisitionVersion => ImageAcquisition,
        MinecraftDumbModeVersion => MinecraftDumbMode,
        _ => null
    };

    public static (ProvisioningPipelineDefinition Pipeline, TrustedRuntimeImage? RuntimeImage) SelectForManaged(
        GameDefinition definition,
        string runtimeType)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeType);

        var candidates = definition.RuntimeImages
            .Where(image => image.RuntimeType == runtimeType && image.IsPinned)
            .ToArray();
        if (candidates.Length != 1 || !IsSupportedV2Image(candidates[0]))
        {
            return (Legacy, null);
        }

        return definition.GameId.Value == "minecraft"
            ? (MinecraftDumbMode, candidates[0])
            : (ImageAcquisition, candidates[0]);
    }

    public static bool UsesTrustedImageAcquisition(string version) =>
        version is ImageAcquisitionVersion or MinecraftDumbModeVersion;

    public static bool RequiresExactMetadata(string version) =>
        version is ImageAcquisitionVersion or MinecraftDumbModeVersion;

    public static bool IsCompletedBeforeReservation(string version, ProvisioningStepDefinition step) =>
        version != MinecraftDumbModeVersion && step.Sequence <= 3;

    private static bool IsSupportedV2Image(TrustedRuntimeImage image) =>
        image.RuntimeType == "docker"
        && image.Platform!.OperatingSystem == "linux"
        && image.Platform.Architecture == "amd64"
        && image.Platform.Variant is null;
}
