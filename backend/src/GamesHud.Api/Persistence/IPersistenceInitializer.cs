namespace GamesHud.Api.Persistence;

public interface IPersistenceInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
