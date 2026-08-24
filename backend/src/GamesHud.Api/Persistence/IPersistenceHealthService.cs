namespace GamesHud.Api.Persistence;

public interface IPersistenceHealthService
{
    Task<PersistenceHealthStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
