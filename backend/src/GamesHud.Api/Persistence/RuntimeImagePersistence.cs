using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.Persistence.ManagedServers;
using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence;

internal static class RuntimeImagePersistence
{
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<ProvisioningOperationRecord>().HasAlternateKey(item => new { item.Id, item.GameServerId });
        builder.Entity<ManagedGameServerRecord>().HasAlternateKey(item => new { item.Id, item.GameId, item.RuntimeType });
        builder.Entity<RuntimeImageIntentRecord>(entity =>
        {
            entity.ToTable("runtime_image_intents", table =>
            {
                table.HasCheckConstraint("CK_runtime_image_digest", "length(ApprovedDigest) = 71 AND substr(ApprovedDigest, 1, 7) = 'sha256:' AND substr(ApprovedDigest, 8) NOT GLOB '*[^0-9a-f]*'");
                table.HasCheckConstraint("CK_runtime_image_id", "VerifiedLocalImageId IS NULL OR (length(VerifiedLocalImageId) = 71 AND substr(VerifiedLocalImageId, 1, 7) = 'sha256:' AND substr(VerifiedLocalImageId, 8) NOT GLOB '*[^0-9a-f]*')");
                table.HasCheckConstraint("CK_runtime_image_state", "(VerificationState = 'pending' AND VerifiedLocalImageId IS NULL AND VerifiedAtUtc IS NULL) OR (VerificationState = 'verified' AND VerifiedLocalImageId IS NOT NULL AND VerifiedAtUtc IS NOT NULL)");
                table.HasCheckConstraint("CK_runtime_image_version", "Version > 0");
                table.HasCheckConstraint("CK_runtime_image_platform", "PlatformOs IN ('linux', 'windows') AND PlatformArchitecture IN ('amd64', 'arm64')");
            });
            entity.HasKey(item => item.OperationId);
            entity.Property(item => item.OperationId).HasMaxLength(32);
            entity.Property(item => item.GameServerId).HasMaxLength(80);
            entity.Property(item => item.GameId).HasMaxLength(120);
            entity.Property(item => item.RuntimeType).HasMaxLength(120);
            entity.Property(item => item.Registry).HasMaxLength(253);
            entity.Property(item => item.Repository).HasMaxLength(255);
            entity.Property(item => item.ApprovedDigest).HasMaxLength(71);
            entity.Property(item => item.PlatformOs).HasMaxLength(20);
            entity.Property(item => item.PlatformArchitecture).HasMaxLength(20);
            entity.Property(item => item.PlatformVariant).HasMaxLength(20);
            entity.Property(item => item.ApprovalSource).HasMaxLength(120);
            entity.Property(item => item.VerifiedLocalImageId).HasMaxLength(71);
            entity.Property(item => item.VerificationState).HasMaxLength(20);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<ProvisioningOperationRecord>().WithMany()
                .HasForeignKey(item => new { item.OperationId, item.GameServerId })
                .HasPrincipalKey(item => new { item.Id, item.GameServerId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ManagedGameServerRecord>().WithMany()
                .HasForeignKey(item => new { item.GameServerId, item.GameId, item.RuntimeType })
                .HasPrincipalKey(item => new { item.Id, item.GameId, item.RuntimeType }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProvisioningReconciliationRecord>(entity =>
        {
            entity.ToTable("provisioning_reconciliations", table =>
                table.HasCheckConstraint("CK_reconciliation_outcome", "Outcome IN ('effect_exists', 'effect_absent', 'ambiguous')"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.OperationId, item.OperationVersion }).IsUnique();
            entity.HasOne<ProvisioningOperationRecord>().WithMany().HasForeignKey(item => item.OperationId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(item => item.Id).HasMaxLength(32);
            entity.Property(item => item.StepId).HasMaxLength(120);
            entity.Property(item => item.Outcome).HasMaxLength(40);
            entity.Property(item => item.AppliedCode).HasMaxLength(120);
            entity.Property(item => item.PriorStatus).HasMaxLength(40);
            entity.Property(item => item.PriorFailureType).HasMaxLength(40);
            entity.Property(item => item.PriorErrorCode).HasMaxLength(120);
        });
    }

    internal static void ValidateChanges(GamesHudDbContext database, DateTimeOffset now)
    {
        foreach (var entry in database.ChangeTracker.Entries<RuntimeImageIntentRecord>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Runtime image intent cannot be deleted.");
            if (entry.State == EntityState.Modified)
            {
                string[] mutable = [nameof(RuntimeImageIntentRecord.VerifiedLocalImageId), nameof(RuntimeImageIntentRecord.VerificationState),
                    nameof(RuntimeImageIntentRecord.VerifiedAtUtc), nameof(RuntimeImageIntentRecord.UpdatedAtUtc), nameof(RuntimeImageIntentRecord.Version)];
                if (entry.Properties.Any(property => property.IsModified && !mutable.Contains(property.Metadata.Name))
                    || entry.Entity.Version != entry.Property(item => item.Version).OriginalValue + 1
                    || entry.Property(item => item.VerifiedLocalImageId).OriginalValue is not null)
                    throw new InvalidOperationException("Approved runtime image intent and confirmed identity are immutable.");
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                _ = RuntimeImageIntentStore.Map(entry.Entity);
                if (entry.State == EntityState.Added) entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
        }
        foreach (var entry in database.ChangeTracker.Entries<ProvisioningReconciliationRecord>())
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Reconciliation audit is append-only.");
        foreach (var entry in database.ChangeTracker.Entries<ProvisioningOperationRecord>().Where(item => item.State == EntityState.Added
            && item.Entity.PipelineVersion == ProvisioningPipelines.ImageAcquisitionVersion))
            if (!database.ChangeTracker.Entries<RuntimeImageIntentRecord>().Any(image => image.State == EntityState.Added
                && image.Entity.OperationId == entry.Entity.Id && image.Entity.GameServerId == entry.Entity.GameServerId))
                throw new InvalidOperationException("V2 operation requires transactional image intent.");
    }
}
