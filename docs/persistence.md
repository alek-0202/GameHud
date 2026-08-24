# Persistence

GamesHud uses EF Core with SQLite as its first local persistence provider.

This foundation is intentionally small. It creates the persistence infrastructure, migration lifecycle, health signal and local transaction boundary needed by future ownership and allocation work. It does not introduce users, tenants, billing, provisioning state, durable game server registration, port reservation, storage reservation or secret persistence.

## Location

The SQLite database is created under the GamesHud data root:

```text
<DataRoot>/system/gameshud.db
```

`DataRoot` comes from `Storage:DataRoot` / `Storage__DataRoot`. When it is empty, the backend uses the existing app-local fallback under `AppContext.BaseDirectory`.

The database path is constructed by the backend. HTTP clients cannot provide a database path, and public API responses must not expose the absolute database path or connection string.

Game server storage remains separate:

```text
<DataRoot>/servers/<gameServerId>
```

The `system` directory is for GamesHud internal state. It is not a game data directory.

## Schema

The initial schema contains only `persistence_metadata`, a technical metadata table used to prove migration and initialization. It is not a resource ownership table.

EF Core migrations are the source of truth for schema changes. Normal application startup uses migrations, not `EnsureCreated`.

Design-time migration commands should work without Docker, VPS access or Palworld configuration:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project backend/src/GamesHud.Api/GamesHud.Api.csproj --startup-project backend/src/GamesHud.Api/GamesHud.Api.csproj --context GamesHudDbContext
```

## Boundaries

Domain models must not depend on `DbContext`, `DbSet`, EF attributes, SQLite APIs, SQL strings or migration types. Persistence-specific records live under the persistence infrastructure.

The current provider is SQLite. Domain behavior must not rely on SQLite-specific behavior so a future PostgreSQL provider can be designed without changing domain rules.

GamesHud uses UTC timestamps for persisted technical records.

## Startup And Health

Startup is fail-fast for persistence initialization. If the configured data root cannot host the database directory or migrations fail, the API startup fails rather than silently running with unknown persistence state.

`GET /api/system/persistence` reports availability, provider and migration status. It must not return absolute filesystem paths, table names, connection strings or secrets.

## Transactions

The persistence foundation provides a local EF Core transaction boundary for future application operations.

A database transaction only covers database writes. It does not make Docker operations, filesystem writes, backups, container lifecycle changes or future provisioning atomic. Future provisioning still needs explicit step state, durable ownership proof, idempotency and compensation or rollback rules.

## Secrets

Secrets are not stored in SQLite in this foundation. Palworld server passwords, Palworld REST credentials, Discord webhook URLs, tokens and private paths remain out of durable application state until SEC-02 defines the dedicated secret model and redaction rules.

## Legacy Resources

The existing Palworld server, including "Amigos e Amigos", remains `LegacyExternal`. GamesHud must not import it into SQLite, adopt its paths, modify its Docker container, change its runtime config or treat existence of a path/container/port as ownership.

## Operations

SQLite file backups are not implemented in this phase. Copying `gameshud.db` while the API is writing is not a safe backup strategy. A future operations task must define database backup, restore and integrity rules.

The database file should live in a controlled backend-only data root. Do not make the `system` directory public. This task does not add chmod/chown automation or claim protection against malicious symlinks, junctions or mount-point races; deeper filesystem hardening belongs to SEC-06 and future provisioning work.
