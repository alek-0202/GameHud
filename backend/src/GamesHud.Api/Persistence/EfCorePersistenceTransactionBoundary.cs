using Microsoft.EntityFrameworkCore.Storage;

namespace GamesHud.Api.Persistence;

public sealed class EfCorePersistenceTransactionBoundary : IPersistenceTransactionBoundary
{
    private readonly GamesHudDbContext _dbContext;

    public EfCorePersistenceTransactionBoundary(GamesHudDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(
        Func<GamesHudDbContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            async (dbContext, token) =>
            {
                await operation(dbContext, token);

                return true;
            },
            cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<GamesHudDbContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await operation(_dbContext, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
