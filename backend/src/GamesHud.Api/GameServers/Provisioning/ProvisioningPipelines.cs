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
    // Activation requires GH-15 and a genuinely approved production catalog digest.
    public static string DefaultVersion => ProvisioningPipeline.Version;
    public static ProvisioningPipelineDefinition Legacy { get; } = new(ProvisioningPipeline.Version, ProvisioningPipeline.Steps);
    public static ProvisioningPipelineDefinition ImageAcquisition { get; } = new(ImageAcquisitionVersion,
        Array.AsReadOnly(ProvisioningPipeline.Steps.Select(step => step.Sequence >= 6 ? step with { Sequence = step.Sequence + 1 } : step)
            .Select(step => step.SideEffectClassification == ProvisioningSideEffectClassifications.Mutation ? step with { MaxAttempts = 3 } : step)
            .Append(new(ProvisioningStepIds.AcquireImage, 6, ProvisioningRetryClassifications.RequiresInspection,
                ProvisioningSideEffectClassifications.Mutation, 3)).OrderBy(step => step.Sequence).ToArray()));

    public static ProvisioningPipelineDefinition? Find(string version) => version switch
    {
        ProvisioningPipeline.Version => Legacy,
        ImageAcquisitionVersion => ImageAcquisition,
        _ => null
    };
}
