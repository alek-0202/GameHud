namespace GamesHud.Api.Persistence;

public sealed record PersistenceHealthStatus(
    bool Available,
    string Provider,
    string MigrationStatus,
    string? AppliedMigration,
    string? ErrorCode);
