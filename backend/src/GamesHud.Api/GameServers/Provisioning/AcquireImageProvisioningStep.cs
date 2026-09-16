using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.Persistence.ManagedServers;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.GameServers.Provisioning;

public sealed class AcquireImageProvisioningStep(IRuntimeImageIntentStore images,
    IRuntimeImageAcquisitionAdapter adapter, IOptions<RuntimeImageAcquisitionOptions> options) : IProvisioningStep
{
    public string Id => ProvisioningStepIds.AcquireImage;

    public async Task<ProvisioningStepResult> ExecuteAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.IsValid)
            return Known(RuntimeImageAcquisitionErrorCodes.InvalidConfiguration, "Runtime image acquisition configuration is invalid.");
        RuntimeImageIntentSnapshot intent;
        try
        {
            intent = await images.LoadAsync(Owner(context), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new ProvisioningCancelledBeforeMutationException(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Known(RuntimeImageAcquisitionErrorCodes.InvalidIntent, "Durable runtime image intent is invalid.");
        }

        RuntimeImageInspectionResult initial;
        try { initial = await adapter.InspectAsync(intent.Image, cancellationToken); }
        catch (OperationCanceledException) { throw new ProvisioningCancelledBeforeMutationException(cancellationToken); }
        if (initial.Status == RuntimeImageInspectionStatuses.Matches)
            return await PersistAsync(intent, initial.LocalImageId!);
        if (initial.Status == RuntimeImageInspectionStatuses.Mismatch)
            return Unknown(RuntimeImageAcquisitionErrorCodes.Mismatch, "The local runtime image does not match the approved identity.");
        if (initial.Status == RuntimeImageInspectionStatuses.ProviderUnavailable)
            return Unknown(RuntimeImageAcquisitionErrorCodes.ProviderUnavailable, "The approved runtime image could not be inspected safely.");
        if (initial.Status != RuntimeImageInspectionStatuses.NotFound)
            return Unknown(RuntimeImageAcquisitionErrorCodes.OutcomeUnknown, "The approved runtime image identity could not be proved.");

        if (cancellationToken.IsCancellationRequested)
            throw new ProvisioningCancelledBeforeMutationException(cancellationToken);
        RuntimeImageAcquisitionResult acquisition;
        try { acquisition = await adapter.AcquireAsync(intent.Image, cancellationToken); }
        catch (OperationCanceledException)
        {
            return Unknown(RuntimeImageAcquisitionErrorCodes.Interrupted, "Runtime image acquisition was interrupted after dispatch.");
        }

        if (cancellationToken.IsCancellationRequested)
            return Unknown(RuntimeImageAcquisitionErrorCodes.Interrupted, "Runtime image acquisition was interrupted after dispatch.");
        RuntimeImageInspectionResult final;
        try { final = await adapter.InspectAsync(intent.Image, cancellationToken); }
        catch (OperationCanceledException)
        {
            return Unknown(RuntimeImageAcquisitionErrorCodes.Interrupted, "Runtime image acquisition final verification was interrupted.");
        }
        if (final.Status == RuntimeImageInspectionStatuses.Matches)
            return await PersistAsync(intent, final.LocalImageId!);
        if (final.Status == RuntimeImageInspectionStatuses.Mismatch)
            return Unknown(RuntimeImageAcquisitionErrorCodes.Mismatch, "Runtime image acquisition could not prove the approved identity.");
        if (final.Status == RuntimeImageInspectionStatuses.Unprovable)
            return Unknown(RuntimeImageAcquisitionErrorCodes.OutcomeUnknown, "Runtime image acquisition final state is unprovable.");
        if (final.Status == RuntimeImageInspectionStatuses.ProviderUnavailable)
            return Unknown(RuntimeImageAcquisitionErrorCodes.ProviderUnavailable, "Runtime image acquisition final verification is unavailable.");

        return acquisition.Status switch
        {
            RuntimeImageAcquisitionStatuses.DigestUnavailable => Known(RuntimeImageAcquisitionErrorCodes.DigestUnavailable,
                "The approved runtime image digest is unavailable."),
            RuntimeImageAcquisitionStatuses.AuthenticationUnsupported => Known(RuntimeImageAcquisitionErrorCodes.AuthenticationUnsupported,
                "The approved runtime image requires unsupported registry authentication."),
            RuntimeImageAcquisitionStatuses.DiskFull => ProvisioningStepResult.Failure(RuntimeImageAcquisitionErrorCodes.DiskFull,
                "Docker reported insufficient storage for the approved runtime image.", ProvisioningFailureTypes.Transient),
            RuntimeImageAcquisitionStatuses.Rejected => Known(RuntimeImageAcquisitionErrorCodes.PullRejected,
                "The registry rejected acquisition of the approved runtime image."),
            RuntimeImageAcquisitionStatuses.Interrupted when acquisition.TimedOut => Unknown(RuntimeImageAcquisitionErrorCodes.Timeout,
                "Runtime image acquisition exceeded its configured timeout."),
            RuntimeImageAcquisitionStatuses.Interrupted => Unknown(RuntimeImageAcquisitionErrorCodes.Interrupted,
                "Runtime image acquisition was interrupted after dispatch."),
            RuntimeImageAcquisitionStatuses.ProviderUnavailable => Unknown(RuntimeImageAcquisitionErrorCodes.ProviderUnavailable,
                "Runtime image provider became unavailable after dispatch."),
            _ => Unknown(RuntimeImageAcquisitionErrorCodes.OutcomeUnknown,
                "Runtime image acquisition completed without a verifiable local image.")
        };
    }

    private async Task<ProvisioningStepResult> PersistAsync(RuntimeImageIntentSnapshot intent,
        GamesHud.Api.GameServers.Definitions.LocalImageId localImageId)
    {
        try
        {
            await images.RecordVerifiedAsync(intent.Owner, intent.Image, localImageId, intent.Version, CancellationToken.None);
            return ProvisioningStepResult.Success();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Unknown(RuntimeImageAcquisitionErrorCodes.Mismatch, "Verified runtime image identity conflicts with durable state.");
        }
    }

    private static RuntimeImageOwner Owner(ProvisioningContext context) => new(context.OperationId,
        context.GameServerId.ToString(), context.ValidatedPlan.GameId.ToString(), context.ValidatedPlan.RuntimeType);
    private static ProvisioningStepResult Known(string code, string message) => ProvisioningStepResult.Failure(code, message);
    private static ProvisioningStepResult Unknown(string code, string message) =>
        ProvisioningStepResult.Failure(code, message, ProvisioningFailureTypes.Unknown);
}

internal sealed class AcquireImageReconciler(IManagedServerStore servers, IRuntimeImageIntentStore images,
    IRuntimeImageAcquisitionAdapter adapter) : IProvisioningStepReconciler
{
    public string StepId => ProvisioningStepIds.AcquireImage;

    public async Task<ProvisioningReconciliationResult> InspectAsync(ProvisioningOperationSnapshot operation,
        ProvisioningStepSnapshot step, CancellationToken cancellationToken)
    {
        try
        {
            var server = await servers.GetManagedServerAsync(operation.GameServerId, cancellationToken);
            if (server is null) return Ambiguous();
            var owner = new RuntimeImageOwner(operation.OperationId, server.Id, server.GameId, server.RuntimeType);
            var intent = await images.LoadAsync(owner, cancellationToken);
            var inspected = await adapter.InspectAsync(intent.Image, cancellationToken);
            if (inspected.Status == RuntimeImageInspectionStatuses.NotFound)
                return intent.VerifiedLocalImageId is null
                    ? new(ProvisioningReconciliationOutcomes.EffectAbsent, "The approved runtime image is absent locally.")
                    : Ambiguous();
            if (inspected.Status != RuntimeImageInspectionStatuses.Matches || inspected.LocalImageId is null)
                return Ambiguous();
            await images.RecordVerifiedAsync(owner, intent.Image, inspected.LocalImageId, intent.Version, cancellationToken);
            return new(ProvisioningReconciliationOutcomes.EffectExists, "The approved runtime image identity was verified locally.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Ambiguous();
        }
    }

    private static ProvisioningReconciliationResult Ambiguous() => new(ProvisioningReconciliationOutcomes.Ambiguous,
        "The approved runtime image identity could not be proved safely.");
}
