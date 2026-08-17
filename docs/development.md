# Development

This guide assumes the GamesHud repository is already open in the current workspace.

The repository can live in any directory. Use relative paths from the repository root and do not rely on machine-specific absolute paths.

When needed, confirm the repository root with:

```powershell
git rev-parse --show-toplevel
```

---

## Prerequisites

- .NET 8 SDK
- Node.js
- npm
- Docker, when testing Docker-backed endpoints or validating container builds locally

Do not install SDKs, runtimes, Docker or global tools automatically as part of routine development tasks. If a missing tool blocks work, report it clearly.

---

## Required Context

Before changing code, read:

- `README.md`
- `ARCHITECTURE.md`
- `AI_RULES.md`
- `ROADMAP.md`
- `docs/development.md`
- Task-specific documentation

For backend work, also read `docs/api-guidelines.md`.
For frontend work, also read `docs/frontend-guidelines.md`.
For deployment work, also read `docs/deployment.md`.

---

## Backend

Restore packages:

```powershell
cd backend
dotnet restore
```

Build:

```powershell
cd backend
dotnet build
```

Test:

```powershell
cd backend
dotnet test
```

Run:

```powershell
cd backend
dotnet run --project src/GamesHud.Api/GamesHud.Api.csproj
```

---

## Backend Configuration

Docker access is configured through application configuration:

- `Docker:Endpoint`
- `Docker__Endpoint`

Metrics are configured through:

- `Metrics:SnapshotIntervalSeconds`
- `Metrics__SnapshotIntervalSeconds`
- `Metrics:RetentionHours`
- `Metrics__RetentionHours`
- `Metrics:HostDiskPath`
- `Metrics__HostDiskPath`

Operational tools are configured through:

- `Notifications:Discord:WebhookUrl`
- `Notifications__Discord__WebhookUrl`
- `Notifications:Discord:CooldownSeconds`
- `Notifications__Discord__CooldownSeconds`
- `Notifications:Discord:Events:ServerStatus`
- `Notifications__Discord__Events__ServerStatus`
- `Notifications:Discord:Events:Backups`
- `Notifications__Discord__Events__Backups`
- `Notifications:Discord:Events:Updates`
- `Notifications__Discord__Events__Updates`
- `Notifications:Discord:Events:PlayerJoinLeave`
- `Notifications__Discord__Events__PlayerJoinLeave`
- `Operations:Palworld:LifecycleTimeoutSeconds`
- `Operations__Palworld__LifecycleTimeoutSeconds`
- `Operations:Palworld:RestartWaitSeconds`
- `Operations__Palworld__RestartWaitSeconds`

For local development, leave the endpoint empty when the Docker client default is correct for the machine. Use local environment variables for machine-specific values and do not commit them.

Docker may be unavailable during local development. In that case:

- `GET /health` should still work.
- Docker-dependent endpoints should return friendly service-unavailable responses.
- Stack traces and sensitive details should not be returned.

The Docker socket is high privilege. Only the backend should access Docker Engine.

Temporary Palworld settings, REST and backups are configured separately:

- `Palworld:ManagedPath`
- `Palworld__ManagedPath`
- `Palworld:BackupPath`
- `Palworld__BackupPath`
- `Palworld:ContainerName`
- `Palworld__ContainerName`
- `Palworld:ConnectionAddress`
- `Palworld__ConnectionAddress`
- `Palworld:Backups:AutomaticEnabled`
- `Palworld__Backups__AutomaticEnabled`
- `Palworld:Backups:AutomaticIntervalMinutes`
- `Palworld__Backups__AutomaticIntervalMinutes`
- `Palworld:Backups:RetentionCount`
- `Palworld__Backups__RetentionCount`
- `Palworld:Backups:RetentionDays`
- `Palworld__Backups__RetentionDays`
- `Palworld:Backups:PreBackupSaveDelaySeconds`
- `Palworld__Backups__PreBackupSaveDelaySeconds`
- `Palworld:Backups:LifecycleTimeoutSeconds`
- `Palworld__Backups__LifecycleTimeoutSeconds`
- `Palworld:RestApi:BaseUrl`
- `Palworld__RestApi__BaseUrl`
- `Palworld:RestApi:Username`
- `Palworld__RestApi__Username`
- `Palworld:RestApi:Password`
- `Palworld__RestApi__Password`
- `Palworld:RestApi:TimeoutSeconds`
- `Palworld__RestApi__TimeoutSeconds`

Use local environment variables for local testing. The backup path must be separate from the managed path. Keep `DISABLE_GENERATE_SETTINGS=true` in the Palworld deployment when testing direct file edits. Do not commit real Palworld paths, container names, passwords, REST credentials, tokens or VPS details.

Optional multi-server development can use a `Servers` collection with `Id`, `Type`, `DisplayName`, `ContainerName`, `ManagedPath`, `BackupPath`, `ConnectionAddress`, and `RestApi`. If this collection is empty, GamesHud uses the singular `Palworld` configuration as a legacy server with id `palworld`.

---

## Backend Endpoints

Health:

```text
GET http://localhost:5258/health
```

Containers:

```text
GET http://localhost:5258/api/containers
```

Container details:

```text
GET http://localhost:5258/api/containers/{containerId}
```

Recent logs:

```text
GET http://localhost:5258/api/containers/{containerId}/logs
GET http://localhost:5258/api/containers/{containerId}/logs?tail=500&timestamps=true&stream=stderr&search=warn
```

Log query parameters:

- `tail`: recent line count. Default `200`, minimum `1`, maximum `2000`.
- `timestamps`: include Docker timestamps. Default `true`.
- `stream`: `all`, `stdout` or `stderr`. Default `all`.
- `search`: optional case-insensitive term filter, maximum `120` characters.

Logs are returned as a snapshot response and are not streamed.

Metrics:

```text
GET http://localhost:5258/api/system/metrics
GET http://localhost:5258/api/system/metrics?historyHours=1
GET http://localhost:5258/api/containers/{containerId}/metrics
GET http://localhost:5258/api/palworld/metrics
GET http://localhost:5258/api/palworld/metrics?historyHours=1
GET http://localhost:5258/api/palworld/metrics?historyHours=6
GET http://localhost:5258/api/palworld/metrics?historyHours=24
```

Metrics use one-shot Docker stats and short in-memory history. They do not expose Docker SDK models.

Lifecycle actions:

```text
POST http://localhost:5258/api/containers/{containerId}/start
POST http://localhost:5258/api/containers/{containerId}/stop
POST http://localhost:5258/api/containers/{containerId}/stop?timeoutSeconds=10
POST http://localhost:5258/api/containers/{containerId}/restart
POST http://localhost:5258/api/containers/{containerId}/restart?timeoutSeconds=10
```

Lifecycle actions are manual only and must be triggered explicitly. Stop and restart accept `timeoutSeconds`; the default is `10`, the minimum is `1`, and the maximum is `120`.

Start returns a friendly success response when the container is already running. Stop returns a friendly success response when the container is already stopped.

Temporary Palworld integration:

```text
GET http://localhost:5258/api/servers
GET http://localhost:5258/api/servers/{serverId}
GET http://localhost:5258/api/servers/{serverId}/overview
GET http://localhost:5258/api/servers/{serverId}/players
POST http://localhost:5258/api/servers/{serverId}/announcements
POST http://localhost:5258/api/servers/{serverId}/players/{userId}/kick
POST http://localhost:5258/api/servers/{serverId}/players/{userId}/ban
POST http://localhost:5258/api/servers/{serverId}/players/unban
GET http://localhost:5258/api/servers/{serverId}/settings
PUT http://localhost:5258/api/servers/{serverId}/settings
GET http://localhost:5258/api/servers/{serverId}/mods
GET http://localhost:5258/api/palworld/config
PUT http://localhost:5258/api/palworld/config
PUT http://localhost:5258/api/palworld/config?restart=true
GET http://localhost:5258/api/palworld/overview
GET http://localhost:5258/api/palworld/players
GET http://localhost:5258/api/palworld/backups
GET http://localhost:5258/api/palworld/backups/{backupId}
GET http://localhost:5258/api/palworld/backups/{backupId}/download
POST http://localhost:5258/api/palworld/backups
POST http://localhost:5258/api/palworld/backups/{backupId}/restore
DELETE http://localhost:5258/api/palworld/backups/{backupId}
GET http://localhost:5258/api/palworld/update
POST http://localhost:5258/api/palworld/update
```

The GET response returns supported settings as a typed metadata list. Password values are represented with `hasValue` and must not expose plaintext `ServerPassword` or `AdminPassword`.

The overview and players endpoints use the Palworld REST API through the backend only. They must not return REST credentials, player IP addresses or raw external contracts.

When `restart=true`, GamesHud stops and starts only the configured Palworld container. Backup restore and manual update also stop and start only the configured Palworld container after strong confirmation and after creating a required backup. These flows must not call compose down, Docker daemon restart, remove, kill, recreate or lifecycle actions for any other container.

Palworld player administration is limited to supported REST actions: announce, kick, ban and unban. Destructive player actions require exact confirmation text. Palworld mod support is inventory-only until a safe and predictable Linux container mod workflow exists.

Operational tools:

```text
GET http://localhost:5258/api/scheduler
POST http://localhost:5258/api/scheduler
POST http://localhost:5258/api/scheduler/{id}/run
GET http://localhost:5258/api/settings/notifications
POST http://localhost:5258/api/settings/notifications/test
```

The scheduler accepts only supported action types and never shell commands. Discord webhook configuration is backend-only and is never returned to the frontend.

---

## Frontend

Install dependencies:

```powershell
cd frontend
npm install
```

Run:

```powershell
cd frontend
npm run dev
```

Build:

```powershell
cd frontend
npm run build
```

Configure the API base URL with a local `.env` created from `frontend/.env.example`:

```text
VITE_API_BASE_URL=http://localhost:5258
```

Local `.env` files must not be committed.

---

## Docker Compose Validation

When Docker is available and it is safe to build images:

```powershell
cd deploy
docker compose config
docker compose build
```

Do not deploy automatically. Do not connect to the VPS automatically.

---

## VPS Homologation Model

GamesHud is developed locally, validated automatically, committed to Git, deployed to the VPS, and tested in a controlled way.

The VPS should be treated as an execution and homologation environment, not as a place to edit source code.

Deployment files are local project files until Docker and VPS validation are completed.

---

## Production Container Safety

The VPS may already run Palworld and Portainer.

GamesHud work must never:

- Stop, restart, recreate, or modify Palworld outside the explicit temporary Palworld config flow.
- Run `docker compose down` in the Palworld project.
- Restart the Docker daemon.
- Restart or shut down the VPS.
- Modify Palworld volumes, networks, or configuration outside the configured Palworld settings file.
- Use Palworld as a test container.
- Use Portainer for destructive tests.

Use only a disposable test container for lifecycle homologation.

---

## Validation

Backend validation:

```powershell
cd backend
dotnet restore
dotnet build
dotnet test
```

Frontend validation:

```powershell
cd frontend
npm run build
```

Deployment validation when Docker is available:

```powershell
cd deploy
docker compose config
docker compose build
```

Run lint only when a lint script is configured.

---

## Development Rules

- Use English for code, file names, class names, method names, variables and technical folders.
- Keep Docker Core behavior independent from game-specific behavior.
- Do not version sensitive configuration, secrets, tokens or local environment files.
- Prefer simple implementations until real requirements justify abstractions.
- Keep backend and frontend responsibilities clearly separated.
- Keep Docker Core read actions separate from manual lifecycle actions.
- Use [API Guidelines](api-guidelines.md) for backend decisions.
- Use [Frontend Guidelines](frontend-guidelines.md) for frontend decisions.
- Use [Deployment Guide](deployment.md) for private deployment and homologation procedures.
- Use [Metrics Guide](metrics.md) for metric source and retention decisions.
- Use [Palworld Backups Guide](palworld-backups.md) for backup and restore behavior.
- Use [Palworld Updates Guide](palworld-updates.md) for manual update checks and update flow behavior.
- Use [Operations Guide](operations.md) for logs, scheduler and Discord notification behavior.
- Use [Palworld Settings Guide](palworld-settings.md) for Palworld setting schema maintenance.
- Use [Palworld REST API Guide](palworld-rest-api.md) for Palworld REST contracts and security rules.
