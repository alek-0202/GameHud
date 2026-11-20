using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Persistence;

public sealed class PersistenceHealthService : IPersistenceHealthService
{
    public const string ProviderName = "sqlite";

    private readonly GamesHudDbContext _dbContext;

    public PersistenceHealthService(GamesHudDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PersistenceHealthStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            if (!canConnect)
            {
                return Unavailable();
            }

            var pendingMigrations = await _dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            var appliedMigrations = await _dbContext.Database
                .GetAppliedMigrationsAsync(cancellationToken);
            var appliedMigration = appliedMigrations.LastOrDefault();

            return new PersistenceHealthStatus(
                Available: true,
                Provider: ProviderName,
                MigrationStatus: pendingMigrations.Any() ? "pending_migrations" : "up_to_date",
                AppliedMigration: appliedMigration,
                ErrorCode: null);
        }
        catch (Exception)
        {
            return Unavailable();
        }
    }

    private static PersistenceHealthStatus Unavailable()
    {
        return new PersistenceHealthStatus(
            Available: false,
            Provider: ProviderName,
            MigrationStatus: "unavailable",
            AppliedMigration: null,
            ErrorCode: "persistence_unavailable");
    }
}
