namespace GamesHud.Api.GameServers.Provisioning;

// ARCH-03 activation gate. GH-15 replaces this with trusted inspect/acquisition.
public sealed class AcquireImageProvisioningStep : IProvisioningStep
{
    public string Id => ProvisioningStepIds.AcquireImage;

    public Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProvisioningStepResult.Failure("runtime_image_acquisition_unavailable",
            "Trusted runtime image acquisition is not available until GH-15."));
    }
}
