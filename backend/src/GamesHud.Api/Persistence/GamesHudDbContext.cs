using GamesHud.Api.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence;

public sealed class GamesHudDbContext : DbContext
{
    public GamesHudDbContext(DbContextOptions<GamesHudDbContext> options)
        : base(options)
    {
    }

    public DbSet<PersistenceMetadataRecord> PersistenceMetadata => Set<PersistenceMetadataRecord>();

    public DbSet<ManagedGameServerRecord> ManagedGameServers => Set<ManagedGameServerRecord>();

    public DbSet<PortReservationRecord> PortReservations => Set<PortReservationRecord>();

    public DbSet<StorageReservationRecord> StorageReservations => Set<StorageReservationRecord>();

    public DbSet<ProvisioningOperationRecord> ProvisioningOperations => Set<ProvisioningOperationRecord>();

    public override int SaveChanges()
    {
        ApplyUtcTimestamps();

        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyUtcTimestamps();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyUtcTimestamps();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyUtcTimestamps();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersistenceMetadataRecord>(entity =>
        {
            entity.ToTable("persistence_metadata");
            entity.HasKey(metadata => metadata.Id);
            entity.Property(metadata => metadata.Id)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(metadata => metadata.Value)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(metadata => metadata.CreatedAt)
                .IsRequired();
            entity.Property(metadata => metadata.UpdatedAt)
                .IsRequired();
        });

        modelBuilder.Entity<ManagedGameServerRecord>(entity =>
        {
            entity.ToTable("managed_game_servers");
            entity.HasKey(server => server.Id);
            entity.Property(server => server.Id)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(server => server.GameId)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(server => server.DisplayName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(server => server.InstallationType)
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(server => server.RuntimeType)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(server => server.LifecycleState)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(server => server.CreatedAtUtc)
                .IsRequired();
            entity.Property(server => server.UpdatedAtUtc)
                .IsRequired();
        });

        modelBuilder.Entity<ProvisioningOperationRecord>(entity =>
        {
            entity.ToTable("provisioning_operations");
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.Id)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(operation => operation.GameServerId)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(operation => operation.Type)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(operation => operation.Status)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(operation => operation.ActiveSlot)
                .HasMaxLength(40);
            entity.Property(operation => operation.CurrentStep)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(operation => operation.StartedAtUtc)
                .IsRequired();
            entity.Property(operation => operation.UpdatedAtUtc)
                .IsRequired();
            entity.Property(operation => operation.CompletedAtUtc);
            entity.Property(operation => operation.ErrorCode)
                .HasMaxLength(120);
            entity.Property(operation => operation.ErrorMessageSafe)
                .HasMaxLength(500);
            entity.HasIndex(operation => operation.GameServerId);
            entity.HasIndex(operation => new
                {
                    operation.GameServerId,
                    operation.Type,
                    operation.ActiveSlot
                })
                .IsUnique();
            entity.HasOne(operation => operation.GameServer)
                .WithMany(server => server.ProvisioningOperations)
                .HasForeignKey(operation => operation.GameServerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PortReservationRecord>(entity =>
        {
            entity.ToTable("port_reservations");
            entity.HasKey(reservation => reservation.Id);
            entity.Property(reservation => reservation.Id)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(reservation => reservation.GameServerId)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(reservation => reservation.PortDefinitionId)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(reservation => reservation.Protocol)
                .HasMaxLength(10)
                .IsRequired();
            entity.Property(reservation => reservation.Port)
                .IsRequired();
            entity.Property(reservation => reservation.Exposure)
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(reservation => reservation.Status)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(reservation => reservation.ProvisioningOperationId)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(reservation => reservation.CreatedAtUtc)
                .IsRequired();
            entity.Property(reservation => reservation.UpdatedAtUtc)
                .IsRequired();
            entity.HasIndex(reservation => new
                {
                    reservation.Protocol,
                    reservation.Port
                })
                .IsUnique();
            entity.HasIndex(reservation => new
                {
                    reservation.GameServerId,
                    reservation.PortDefinitionId
                })
                .IsUnique();
            entity.HasOne(reservation => reservation.GameServer)
                .WithMany(server => server.PortReservations)
                .HasForeignKey(reservation => reservation.GameServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(reservation => reservation.ProvisioningOperation)
                .WithMany(operation => operation.PortReservations)
                .HasForeignKey(reservation => reservation.ProvisioningOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StorageReservationRecord>(entity =>
        {
            entity.ToTable("storage_reservations");
            entity.HasKey(reservation => reservation.Id);
            entity.Property(reservation => reservation.Id)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(reservation => reservation.GameServerId)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(reservation => reservation.StorageDefinitionId)
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(reservation => reservation.RelativePath)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(reservation => reservation.Ownership)
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(reservation => reservation.Status)
                .HasMaxLength(80)
                .IsRequired();
            entity.Property(reservation => reservation.ProvisioningOperationId)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(reservation => reservation.CreatedAtUtc)
                .IsRequired();
            entity.Property(reservation => reservation.UpdatedAtUtc)
                .IsRequired();
            entity.HasIndex(reservation => reservation.RelativePath)
                .IsUnique();
            entity.HasIndex(reservation => new
                {
                    reservation.GameServerId,
                    reservation.StorageDefinitionId
                })
                .IsUnique();
            entity.HasOne(reservation => reservation.GameServer)
                .WithMany(server => server.StorageReservations)
                .HasForeignKey(reservation => reservation.GameServerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(reservation => reservation.ProvisioningOperation)
                .WithMany(operation => operation.StorageReservations)
                .HasForeignKey(reservation => reservation.ProvisioningOperationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void ApplyUtcTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<PersistenceMetadataRecord>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = ToUtc(entry.Entity.CreatedAt, now);
                entry.Entity.UpdatedAt = ToUtc(entry.Entity.UpdatedAt, now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.CreatedAt = ToUtc(entry.Entity.CreatedAt, now);
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ManagedGameServerRecord>())
        {
            ApplyCreatedUpdatedTimestamps(
                entry.State,
                () => entry.Entity.CreatedAtUtc,
                value => entry.Entity.CreatedAtUtc = value,
                () => entry.Entity.UpdatedAtUtc,
                value => entry.Entity.UpdatedAtUtc = value,
                now);
        }

        foreach (var entry in ChangeTracker.Entries<PortReservationRecord>())
        {
            ApplyCreatedUpdatedTimestamps(
                entry.State,
                () => entry.Entity.CreatedAtUtc,
                value => entry.Entity.CreatedAtUtc = value,
                () => entry.Entity.UpdatedAtUtc,
                value => entry.Entity.UpdatedAtUtc = value,
                now);
        }

        foreach (var entry in ChangeTracker.Entries<StorageReservationRecord>())
        {
            ApplyCreatedUpdatedTimestamps(
                entry.State,
                () => entry.Entity.CreatedAtUtc,
                value => entry.Entity.CreatedAtUtc = value,
                () => entry.Entity.UpdatedAtUtc,
                value => entry.Entity.UpdatedAtUtc = value,
                now);
        }

        foreach (var entry in ChangeTracker.Entries<ProvisioningOperationRecord>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.StartedAtUtc = ToUtc(entry.Entity.StartedAtUtc, now);
                entry.Entity.UpdatedAtUtc = ToUtc(entry.Entity.UpdatedAtUtc, now);
                entry.Entity.CompletedAtUtc = entry.Entity.CompletedAtUtc?.ToUniversalTime();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.StartedAtUtc = ToUtc(entry.Entity.StartedAtUtc, now);
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.CompletedAtUtc = entry.Entity.CompletedAtUtc?.ToUniversalTime();
            }
        }
    }

    private static void ApplyCreatedUpdatedTimestamps(
        EntityState state,
        Func<DateTimeOffset> getCreatedAt,
        Action<DateTimeOffset> setCreatedAt,
        Func<DateTimeOffset> getUpdatedAt,
        Action<DateTimeOffset> setUpdatedAt,
        DateTimeOffset now)
    {
        if (state == EntityState.Added)
        {
            setCreatedAt(ToUtc(getCreatedAt(), now));
            setUpdatedAt(ToUtc(getUpdatedAt(), now));
        }
        else if (state == EntityState.Modified)
        {
            setCreatedAt(ToUtc(getCreatedAt(), now));
            setUpdatedAt(now);
        }
    }

    private static DateTimeOffset ToUtc(DateTimeOffset value, DateTimeOffset fallback)
    {
        if (value == default)
        {
            return fallback;
        }

        return value.ToUniversalTime();
    }
}
