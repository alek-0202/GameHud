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
