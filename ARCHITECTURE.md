# GamesHud Architecture

## Vision

GamesHud is a generic web platform for managing Docker containers, game servers and infrastructure.

The long-term goal is a production-ready management platform with a generic Docker Core and optional plugins for application-specific behavior.

The Docker Core must remain independent from game-specific plugins.

---

## High Level Flow

```text
React frontend
      |
      | HTTP / REST
      v
.NET API
      |
      | Docker Core services
      v
Docker Engine API
      |
      v
Docker containers
```

Frontend code communicates only with the GamesHud API.
Backend services communicate with Docker Engine through Docker.DotNet.

---

## Core Responsibilities

The Docker Core owns generic infrastructure features only:

- Container listing
- Container details
- Recent logs snapshots
- Manual container lifecycle actions
- Host, Docker summary and container metrics
- Safe operational schedules for supported actions
- Optional backend-only operational notifications
- Future image, network and volume views
- Future backup and restore foundations

The Docker Core must not contain behavior specific to Palworld, Minecraft, Terraria, Discord Bot or any other game or application.

---

## Plugin Responsibilities

Plugins are now introduced as a minimal foundation for application-specific features.

Current minimal plugin contract:

- Id
- Game type
- Display name
- Capabilities

Current capabilities:

- overview
- settings
- players
- backups
- update
- logs

Examples of plugin responsibilities:

- Game server status
- Player lists
- RCON or application-specific APIs
- Game configuration editing
- Game-specific backup and restore workflows
- Controlled restart or update workflows

The first plugin type is Palworld. This is not a full plugin SDK and must stay small until another real game server requires more surface area.

## Temporary Palworld Integration

A temporary personal Palworld settings editor, REST overview, backup manager and manual update flow exist while the plugin system is being introduced incrementally.

This feature is intentionally isolated under Palworld-specific backend and frontend boundaries. It is not part of Docker Core and must not add Palworld rules to `DockerContainerService`.

Current responsibilities:

- Locate `PalWorldSettings.ini` inside the configured managed path.
- Read and update only supported Palworld settings.
- Preserve unknown INI settings.
- Create a backup before writing.
- Never return plaintext `ServerPassword` or raw INI content.
- Optionally stop and start only the configured Palworld container after saving.
- Read native Palworld REST API info, players, settings and metrics through the backend only.
- Send Palworld REST announcements and player kick, ban and unban requests through the backend only.
- Create, list, download, restore and delete Palworld backups from configured backend paths only.
- Create a pre-restore backup before every restore.
- Check and apply Palworld server updates only for the configured Palworld container.
- Never expose Palworld REST API credentials, player IP addresses or raw external contracts.
- Detect local mod files as inventory only. GamesHud must not download, enable, disable or execute mods in the current Linux container setup.

This temporary integration should keep migrating onto server-scoped Palworld plugin contracts.

---

## Backend Boundaries

Controllers must stay thin:

- Validate HTTP input.
- Delegate work to services.
- Produce HTTP responses.

Controllers must not access Docker.DotNet or other external SDK types directly.

Docker SDK access belongs in Docker Core services. SDK models must be mapped into GamesHud API contracts before returning responses.

See [API Guidelines](docs/api-guidelines.md) for permanent backend rules.

---

## API Contracts

The API exposes GamesHud contracts, not Docker SDK models.

Current container list contract:

- `id`
- `name`
- `image`
- `state`
- `status`

Current container details contract:

- `id`
- `name`
- `image`
- `imageId`
- `state`
- `status`
- `createdAt`
- `startedAt`
- `finishedAt`
- `restartCount`
- `platform`
- `driver`
- `ports`
- `mounts`
- `networks`
- filtered `labels`

Current logs contract:

- `containerId`
- `lines`
- `retrievedAt`

Current lifecycle action contract:

- `containerId`
- `action`
- `success`
- `message`
- `previousState`
- `currentState`
- `completedAt`

Environment variables must not be exposed through public container contracts. Raw Docker inspect payloads must not be returned.

---

## Docker Access

Docker access is configured through application configuration, including:

- `Docker:Endpoint`
- `Docker__Endpoint`

Configuration values are environment-specific and must not be hardcoded in code or permanent documentation.

Temporary Palworld configuration uses environment-specific values:

- `Palworld:ManagedPath`
- `Palworld__ManagedPath`
- `Palworld:BackupPath`
- `Palworld__BackupPath`
- `Palworld:ContainerName`
- `Palworld__ContainerName`
- `Palworld:ConnectionAddress`
- `Palworld__ConnectionAddress`
- `Palworld:RestApi:*`
- `Palworld__RestApi__*`

The optional multi-server registry uses a `Servers` collection. If the collection is empty, GamesHud creates a legacy `palworld` server from the singular `Palworld` configuration. Server descriptors returned to the frontend must not include REST usernames, passwords, base URLs or filesystem paths.

The client must never provide filesystem paths.

The Docker socket is high privilege and must be accessible only to the backend process. Frontend code must never access Docker Engine, Docker sockets, credentials or certificates directly.

---

## Current Docker Core State

Implemented:

- `GET /api/containers`
- `GET /api/containers/{containerId}`
- `GET /api/containers/{containerId}/logs`
- `POST /api/containers/{containerId}/start`
- `POST /api/containers/{containerId}/stop`
- `POST /api/containers/{containerId}/restart`
- Frontend container list
- Frontend details view
- Recent logs snapshot view
- Log search, stream filters, timestamps and severity metadata
- Manual lifecycle actions in the details view
- Host, Docker summary and container metrics
- Short in-memory metrics history
- In-memory operational scheduler for supported action types only
- Backend-only Discord webhook notification status and test endpoint
- Friendly Docker-unavailable responses
- Tests for contracts, mapping and error handling

Not implemented yet:

- Authentication
- Authorization
- Full plugin SDK
- Generic server creation
- Real-time logs
- SignalR or WebSockets

Read-only endpoints must not alter container state. Lifecycle endpoints are explicit manual actions limited to start, stop and restart.

The temporary Palworld config endpoints are separate from Docker Core:

- `GET /api/palworld/config`
- `PUT /api/palworld/config`
- `PUT /api/palworld/config?restart=true`
- `GET /api/palworld/overview`
- `GET /api/palworld/players`
- `GET /api/palworld/metrics`
- `GET /api/palworld/backups`
- `GET /api/palworld/backups/{backupId}`
- `GET /api/palworld/backups/{backupId}/download`
- `POST /api/palworld/backups`
- `POST /api/palworld/backups/{backupId}/restore`
- `DELETE /api/palworld/backups/{backupId}`
- `GET /api/palworld/update`
- `POST /api/palworld/update`
- `GET /api/servers`
- `GET /api/servers/{serverId}`
- `GET /api/servers/{serverId}/overview`
- `GET /api/servers/{serverId}/players`
- `POST /api/servers/{serverId}/announcements`
- `POST /api/servers/{serverId}/players/{userId}/kick`
- `POST /api/servers/{serverId}/players/{userId}/ban`
- `POST /api/servers/{serverId}/players/unban`
- `GET /api/servers/{serverId}/settings`
- `PUT /api/servers/{serverId}/settings`
- `GET /api/servers/{serverId}/mods`

Operational endpoints:

- `GET /api/scheduler`
- `POST /api/scheduler`
- `POST /api/scheduler/{id}/run`
- `GET /api/settings/notifications`
- `POST /api/settings/notifications/test`

The scheduler must never accept shell commands, Docker exec commands, compose operations or arbitrary user-provided operations. It may execute only action types explicitly supported by GamesHud.

The metrics endpoints are part of the generic operational surface:

- `GET /api/system/metrics`
- `GET /api/containers/{containerId}/metrics`

---

## Deployment Architecture

GamesHud is prepared to run through Docker Compose for private VPS homologation. Deployment files are implemented locally and still require Docker/VPS validation before claiming a completed deployment.

```text
Browser through SSH tunnel
      |
      v
VPS loopback published frontend port
      |
      v
gameshud-frontend container
nginx static frontend
      |
      | /api and /health proxy over Compose network
      v
gameshud-api container
.NET API
      |
      | Docker socket mount
      v
Docker Engine on VPS
```

The Compose deployment uses a GamesHud-owned network created by Compose. It must not use Portainer networks or unrelated Palworld project networks.

The frontend is published only on VPS loopback while authentication does not exist. The API is not published to the host by default and is reached by the frontend over the internal Compose network.

For the temporary Palworld REST integration, `gameshud-api` also joins the external Docker network `gameshud-palworld`. This preserves private API-to-Palworld connectivity across redeploys. The frontend does not join that network.

### Deployment Security Rules

- Only `gameshud-api` may mount the Docker socket.
- `gameshud-frontend` must never mount the Docker socket.
- The Docker socket must never be exposed over TCP.
- GamesHud must not enable Docker remote API.
- No Docker credentials or host-level secrets may be sent to the frontend.
- No database is introduced until there is real durable application state beyond filesystem backups.
- Palworld backups must use a separate backend-only mount from the managed Palworld data path.

### VPS Operational Boundaries

The VPS may already host production containers such as Palworld and Portainer.

GamesHud development, deployment, testing, and maintenance must never:

- Stop, restart, recreate, or modify Palworld outside the explicit temporary Palworld config flow.
- Run `docker compose down` in the Palworld project.
- Restart the Docker daemon.
- Restart or shut down the VPS.
- Modify Palworld volumes, networks, or configuration outside the configured Palworld settings file.
- Use Palworld as a test container.
- Use Portainer for destructive tests.

Lifecycle homologation must use a disposable container created specifically for GamesHud.

---

## Development Principles

- Simplicity before abstraction.
- Build only what is needed.
- Avoid overengineering.
- Prefer clear service boundaries over speculative layers.
- Introduce abstractions only when duplication, SDK isolation or testability justifies them.
- Keep Docker Core generic.
- Keep plugin-specific behavior outside the Core.

Do not add Repository, Unit of Work, CQRS, Mediator, event bus or full Clean Architecture layers without a concrete requirement.

---

## Documentation Map

- [README](README.md): public overview, current status and quick start.
- [AI Rules](AI_RULES.md): permanent operating rules for AI assistants.
- [API Guidelines](docs/api-guidelines.md): backend rules.
- [Frontend Guidelines](docs/frontend-guidelines.md): frontend rules.
- [Development Guide](docs/development.md): local setup and commands.
- [Deployment Guide](docs/deployment.md): private Compose deployment and homologation.
- [Roadmap](ROADMAP.md): planned delivery order and feature status.
