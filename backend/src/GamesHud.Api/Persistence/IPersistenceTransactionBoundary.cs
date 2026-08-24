namespace GamesHud.Api.Persistence;

public interface IPersistenceTransactionBoundary
{
    Task ExecuteAsync(
        Func<GamesHudDbContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        Func<GamesHudDbContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
