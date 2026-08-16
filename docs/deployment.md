# Deployment

GamesHud deployment is currently private and intended for controlled homologation on a Linux VPS.

The target VPS runs Ubuntu 24.04 LTS and may already host production Docker containers. Treat the VPS as an execution environment, not as a code editing environment.

## Safety Rules

GamesHud work must never:

- Stop, restart, recreate, or modify Palworld outside the explicit temporary Palworld config flow.
- Run `docker compose down` in the Palworld project.
- Restart the Docker daemon.
- Restart or shut down the VPS.
- Modify Palworld volumes, networks, or configuration outside the configured Palworld settings file.
- Use Palworld as a test container.
- Use Portainer for destructive tests.

Use a disposable container for lifecycle homologation.

## Deployment Location

The expected VPS location is:

```text
/opt/containers/gameshud
```

This path is operational documentation only. It must not be hardcoded in application code.

## Compose Services

The deployment uses:

- `gameshud-api`
- `gameshud-frontend`

The Compose project creates its own default network. Do not attach GamesHud to Palworld or Portainer networks.

## Ports

Internal ports:

- API: `8080`
- Frontend nginx: `80`

Suggested loopback publishing:

- Frontend: `127.0.0.1:8088:80`

The API is not published to the host by default. The frontend proxies `/api` and `/health` to the API over the private Compose network.

## Environment Variables

Use `deploy/.env.example` as the template.

```text
GAMESHUD_FRONTEND_PORT=8088
GAMESHUD_DOCKER_ENDPOINT=unix:///var/run/docker.sock
GAMESHUD_METRICS_SNAPSHOT_INTERVAL_SECONDS=60
GAMESHUD_METRICS_RETENTION_HOURS=24
GAMESHUD_METRICS_HOST_DISK_PATH=/
GAMESHUD_PALWORLD_PATH=/path/to/palworld/data
GAMESHUD_PALWORLD_BACKUP_PATH=/path/to/gameshud/palworld-backups
GAMESHUD_PALWORLD_CONTAINER=palworld-server-example
GAMESHUD_PALWORLD_CONNECTION_ADDRESS=palworld.example.test:8211
GAMESHUD_PALWORLD_BACKUPS_AUTOMATIC_ENABLED=false
GAMESHUD_PALWORLD_BACKUPS_AUTOMATIC_INTERVAL_MINUTES=360
GAMESHUD_PALWORLD_BACKUPS_RETENTION_COUNT=24
GAMESHUD_PALWORLD_BACKUPS_RETENTION_DAYS=7
GAMESHUD_PALWORLD_BACKUPS_PRE_SAVE_DELAY_SECONDS=2
GAMESHUD_PALWORLD_BACKUPS_LIFECYCLE_TIMEOUT_SECONDS=30
GAMESHUD_PALWORLD_REST_BASE_URL=http://internal-palworld-rest:8212
GAMESHUD_PALWORLD_REST_USERNAME=admin
GAMESHUD_PALWORLD_REST_PASSWORD=change-me
GAMESHUD_PALWORLD_REST_TIMEOUT_SECONDS=5
```

Do not commit a real `.env`.

## Volumes

The API mounts the Docker socket:

```text
/var/run/docker.sock:/var/run/docker.sock
```

For the temporary Palworld config editor and backup manager, the API also mounts the configured Palworld data directory:

```text
${GAMESHUD_PALWORLD_PATH}:/managed/palworld
```

Palworld backups use a separate API-only mount:

```text
${GAMESHUD_PALWORLD_BACKUP_PATH}:/managed/palworld-backups
```

The API receives:

```text
Palworld__ManagedPath=/managed/palworld
Palworld__BackupPath=/managed/palworld-backups
Metrics__SnapshotIntervalSeconds=${GAMESHUD_METRICS_SNAPSHOT_INTERVAL_SECONDS}
Metrics__RetentionHours=${GAMESHUD_METRICS_RETENTION_HOURS}
Metrics__HostDiskPath=${GAMESHUD_METRICS_HOST_DISK_PATH}
Palworld__ContainerName=${GAMESHUD_PALWORLD_CONTAINER}
Palworld__ConnectionAddress=${GAMESHUD_PALWORLD_CONNECTION_ADDRESS}
Palworld__Backups__AutomaticEnabled=${GAMESHUD_PALWORLD_BACKUPS_AUTOMATIC_ENABLED}
Palworld__Backups__AutomaticIntervalMinutes=${GAMESHUD_PALWORLD_BACKUPS_AUTOMATIC_INTERVAL_MINUTES}
Palworld__Backups__RetentionCount=${GAMESHUD_PALWORLD_BACKUPS_RETENTION_COUNT}
Palworld__Backups__RetentionDays=${GAMESHUD_PALWORLD_BACKUPS_RETENTION_DAYS}
Palworld__Backups__PreBackupSaveDelaySeconds=${GAMESHUD_PALWORLD_BACKUPS_PRE_SAVE_DELAY_SECONDS}
Palworld__Backups__LifecycleTimeoutSeconds=${GAMESHUD_PALWORLD_BACKUPS_LIFECYCLE_TIMEOUT_SECONDS}
Palworld__RestApi__BaseUrl=${GAMESHUD_PALWORLD_REST_BASE_URL}
Palworld__RestApi__Username=${GAMESHUD_PALWORLD_REST_USERNAME}
Palworld__RestApi__Password=${GAMESHUD_PALWORLD_REST_PASSWORD}
Palworld__RestApi__TimeoutSeconds=${GAMESHUD_PALWORLD_REST_TIMEOUT_SECONDS}
```

The frontend mounts no Docker socket and receives no Docker credentials.
The frontend does not receive the Palworld data directory.
The frontend does not receive the Palworld backup directory.
The frontend does not receive Palworld REST API credentials.

No database is created in this phase. Palworld backup archives and sidecar metadata use the configured backend-only backup mount.

## Metrics Notes

GamesHud collects host, Docker and Palworld metrics as lightweight snapshots.

- Host metrics read Linux `/proc` files and filesystem usage.
- Container metrics use Docker Engine one-shot stats.
- Metrics history is kept in API memory with default 60-second snapshots and 24-hour retention.
- No Prometheus, Grafana, SQLite volume or database is introduced in this phase.

See [Metrics Guide](metrics.md) for contracts and retention details.

## Temporary Palworld Settings Notes

The Palworld integration is a personal settings editor, not the planned plugin system.

The target Palworld deployment should keep generated settings disabled when using file-based edits:

```text
DISABLE_GENERATE_SETTINGS=true
```

Some Palworld settings require a server restart before they take effect. The GamesHud Save & Restart action stops and starts only the configured Palworld container and is disabled when there are no pending changes. Connected players will be disconnected and the server will have temporary downtime.

GamesHud needs write access to the configured Palworld directory. Do not commit real server paths, passwords, container names, IPs, users, keys or secrets.

See [Palworld Settings Guide](palworld-settings.md) for the schema categories, sources and maintenance rules.

## Temporary Palworld Backups Notes

GamesHud can create, list, download, restore and delete backups for the configured Palworld managed directory.

- Only the API receives `Palworld__ManagedPath` and `Palworld__BackupPath`.
- `Palworld__BackupPath` must be separate from `Palworld__ManagedPath`; do not place backups inside the managed data directory.
- Manual backup asks the Palworld REST API to save the world first when REST is configured, waits briefly, then creates a `.tar.gz` archive.
- Restore is destructive and requires strong confirmation in the UI and API.
- Restore stops only the configured Palworld container, creates a `pre-restore` backup, restores the selected archive, starts only the configured Palworld container and checks health.
- Automatic backups are controlled by application configuration and are disabled by default.
- Retention applies to automatic backups and must never remove the last valid backup.

Do not point `GAMESHUD_PALWORLD_BACKUP_PATH` at an existing unrelated backup archive directory without reviewing expected retention behavior.

See [Palworld Backups Guide](palworld-backups.md) for contracts and operating details.

## Temporary Palworld REST Notes

GamesHud uses the native Palworld REST API for overview, players, metrics and world-save requests before backups. RCON is not used.

The Palworld REST API must remain private. Do not publish port `8212` publicly for GamesHud. Configure `GAMESHUD_PALWORLD_REST_BASE_URL` with an address reachable from the backend container through a private/internal path.

See [Palworld REST API Guide](palworld-rest-api.md) for contracts and security rules.

## Manual VPS Deployment

On the VPS, place the repository contents under the deployment directory, then run:

```bash
cd /opt/containers/gameshud/deploy
docker compose build
docker compose up -d
docker compose ps
docker compose logs
```

For updates, prefer:

```bash
cd /opt/containers/gameshud/deploy
docker compose up -d --build
```

Do not run `docker compose down` unless there is a specific, reviewed reason.

## SSH Tunnel Access

Because authentication does not exist yet, do not expose GamesHud publicly.

From your local machine:

```bash
ssh -L 8088:127.0.0.1:8088 user@your-vps-host
```

Then open:

```text
http://localhost:8088
```

Do not document real VPS IPs, hostnames, users, keys, or credentials in the repository.

## Disposable Lifecycle Test Container

Create a disposable container only when manually homologating lifecycle features:

```bash
docker run -d --name gameshud-lifecycle-test nginx:alpine
```

Use GamesHud to validate:

- list
- details
- logs
- stop
- start
- restart

When testing is complete, remove only the disposable container:

```bash
docker rm -f gameshud-lifecycle-test
```

Do not use Palworld, Portainer, or GamesHud's own containers for generic lifecycle tests.

## Security Notes

The Docker socket grants host-level control. Treat it as equivalent to host administration.

- Only the API may access the Docker socket.
- The frontend must never access the socket.
- The socket must never be exposed over TCP.
- The socket must never be published.
- Do not enable Docker remote API.
- Do not send Docker credentials to the frontend.

Until authentication exists, GamesHud must remain private and accessible only through SSH tunneling.
