using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.Models;
using GamesHud.Api.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence.ManagedServers;

public sealed record RuntimeImageOwner(string OperationId, string GameServerId, string GameId, string RuntimeType);

public sealed record RuntimeImageIntentSnapshot(RuntimeImageOwner Owner, TrustedRuntimeImage Image,
    LocalImageId? VerifiedLocalImageId, int Version);

// Constructed only after loading validated durable state. Never an HTTP contract.
public sealed class VerifiedRuntimeImage
{
    internal VerifiedRuntimeImage(RuntimeImageIntentSnapshot snapshot)
    {
        Owner = snapshot.Owner;
        ApprovedImage = snapshot.Image;
        LocalImageId = snapshot.VerifiedLocalImageId ?? throw new InvalidOperationException("Runtime image is not verified.");
    }
    public RuntimeImageOwner Owner { get; }
    public TrustedRuntimeImage ApprovedImage { get; }
    public LocalImageId LocalImageId { get; }
}

public interface IRuntimeImageIntentStore
{
    Task<RuntimeImageIntentSnapshot> LoadAsync(RuntimeImageOwner owner, CancellationToken cancellationToken = default);
    Task<VerifiedRuntimeImage> LoadVerifiedAsync(RuntimeImageOwner owner, CancellationToken cancellationToken = default);
    Task<RuntimeImageIntentSnapshot> RecordVerifiedAsync(RuntimeImageOwner owner, TrustedRuntimeImage observedIntent,
        LocalImageId imageId, int expectedVersion, CancellationToken cancellationToken = default);
}

public sealed class RuntimeImageIntentStore(GamesHudDbContext database, IPersistenceTransactionBoundary transactions)
    : IRuntimeImageIntentStore
{
    public async Task<RuntimeImageIntentSnapshot> LoadAsync(RuntimeImageOwner owner, CancellationToken cancellationToken = default)
    {
        var record = await database.RuntimeImageIntents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationId == owner.OperationId, cancellationToken)
            ?? throw new InvalidOperationException("Durable runtime image intent is missing.");
        await ValidateOwnerAsync(owner, record, cancellationToken);
        return Map(record);
    }

    public async Task<VerifiedRuntimeImage> LoadVerifiedAsync(RuntimeImageOwner owner, CancellationToken cancellationToken = default) =>
        new(await LoadAsync(owner, cancellationToken));

    public async Task<RuntimeImageIntentSnapshot> RecordVerifiedAsync(RuntimeImageOwner owner, TrustedRuntimeImage observedIntent,
        LocalImageId imageId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageId);
        try
        {
            return await transactions.ExecuteAsync(async (db, token) =>
            {
                var record = await db.RuntimeImageIntents.SingleAsync(item => item.OperationId == owner.OperationId, token);
                await ValidateOwnerAsync(owner, record, token);
                var snapshot = Map(record);
                if (snapshot.Image != observedIntent) throw new InvalidOperationException("Observed image does not match approved intent.");
                if (snapshot.VerifiedLocalImageId is not null)
                {
                    if (snapshot.VerifiedLocalImageId != imageId) throw new InvalidOperationException("Verified runtime image identity conflicts.");
                    return snapshot;
                }
                if (record.Version != expectedVersion) throw new ProvisioningConcurrencyException("Runtime image intent version conflict.");
                var operation = await db.ProvisioningOperations.Include(item => item.Steps)
                    .SingleAsync(item => item.Id == owner.OperationId, token);
                var step = operation.Steps.Single(item => item.StepId == ProvisioningStepIds.AcquireImage);
                if (operation.ActiveSlot is null
                    || operation.Status is not ProvisioningOperationStatuses.Running
                        and not ProvisioningOperationStatuses.Failed and not ProvisioningOperationStatuses.Cancelled
                    || step.Status is not ProvisioningStepStatuses.Running and not ProvisioningStepStatuses.Failed)
                    throw new InvalidOperationException("Image verification requires an active acquisition step.");
                record.VerifiedLocalImageId = imageId.Value;
                record.VerificationState = "verified";
                record.VerifiedAtUtc = DateTimeOffset.UtcNow;
                record.Version++;
                return Map(record);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ProvisioningConcurrencyException("Runtime image verification changed concurrently.", exception);
        }
    }

    private async Task ValidateOwnerAsync(RuntimeImageOwner owner, RuntimeImageIntentRecord record, CancellationToken token)
    {
        if (record.OperationId != owner.OperationId || record.GameServerId != owner.GameServerId
            || record.GameId != owner.GameId || record.RuntimeType != owner.RuntimeType
            || !await database.ProvisioningOperations.AsNoTracking().AnyAsync(item => item.Id == owner.OperationId
                && item.GameServerId == owner.GameServerId && item.PipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion, token)
            || !await database.ManagedGameServers.AsNoTracking().AnyAsync(item => item.Id == owner.GameServerId
                && item.GameId == owner.GameId && item.RuntimeType == owner.RuntimeType
                && item.InstallationType == ManagedInstallationTypes.Managed, token))
            throw new InvalidOperationException("Runtime image ownership is invalid.");
    }

    internal static RuntimeImageIntentRecord Create(string operationId, ManagedServerProvisioningPlan plan)
    {
        var image = plan.RuntimeImage!;
        return new RuntimeImageIntentRecord
        {
            OperationId = operationId, GameServerId = plan.GameServerId, GameId = plan.GameId, RuntimeType = plan.RuntimeType,
            Registry = image.Registry!, Repository = image.Repository, ApprovedDigest = image.ApprovedDigest!.Value,
            PlatformOs = image.Platform!.OperatingSystem, PlatformArchitecture = image.Platform.Architecture,
            PlatformVariant = image.Platform.Variant, ApprovalSource = image.Source
        };
    }

    internal static RuntimeImageIntentSnapshot Map(RuntimeImageIntentRecord record)
    {
        if (record.Version < 1 || (record.VerificationState == "pending"
                ? record.VerifiedLocalImageId is not null || record.VerifiedAtUtc is not null
                : record.VerificationState != "verified" || record.VerifiedLocalImageId is null || record.VerifiedAtUtc is null))
            throw new InvalidOperationException("Durable runtime image verification is invalid.");
        var image = new TrustedRuntimeImage(record.RuntimeType, record.Registry, record.Repository,
            new ApprovedImageDigest(record.ApprovedDigest), new RuntimeImagePlatform(record.PlatformOs,
                record.PlatformArchitecture, record.PlatformVariant), record.ApprovalSource);
        if (image.Registry != record.Registry || image.Repository != record.Repository
            || image.ApprovedDigest!.Value != record.ApprovedDigest || image.Platform!.OperatingSystem != record.PlatformOs
            || image.Platform.Architecture != record.PlatformArchitecture || image.Platform.Variant != record.PlatformVariant
            || image.Source != record.ApprovalSource)
            throw new InvalidOperationException("Durable runtime image intent is not canonical.");
        return new(new(record.OperationId, record.GameServerId, record.GameId, record.RuntimeType), image,
            record.VerifiedLocalImageId is null ? null : new LocalImageId(record.VerifiedLocalImageId), record.Version);
    }
}
