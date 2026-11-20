namespace GamesHud.Api.Persistence.Contracts;

public sealed record PersistenceStatusResponse(
    bool Available,
    string Provider,
    string MigrationStatus,
    string? AppliedMigration,
    string? ErrorCode);
