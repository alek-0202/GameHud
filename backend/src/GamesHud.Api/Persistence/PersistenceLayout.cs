namespace GamesHud.Api.Persistence;

public sealed record PersistenceLayout(
    string DataRoot,
    string SystemRoot,
    string DatabasePath);
