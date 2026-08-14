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
- Future image, network and volume views
- Future metrics
- Future backup and restore foundations

The Docker Core must not contain behavior specific to Palworld, Minecraft, Terraria, Discord Bot or any other game or application.

---

## Plugin Responsibilities

Plugins are planned for application-specific features.

Examples of future plugin responsibilities:

- Game server status
- Player lists
- RCON or application-specific APIs
- Game configuration editing
- Game-specific backup and restore workflows
- Controlled restart or update workflows

Plugins are not implemented yet.

## Temporary Palworld Quick Config

A temporary personal Palworld settings editor exists before the planned plugin system.

This feature is intentionally isolated under Palworld-specific backend and frontend boundaries. It is not part of Docker Core and must not add Palworld rules to `DockerContainerService`.

Current responsibilities:

- Locate `PalWorldSettings.ini` inside the configured managed path.
- Read and update only supported Palworld settings.
- Preserve unknown INI settings.
- Create a backup before writing.
- Never return plaintext `ServerPassword` or raw INI content.
- Optionally stop and start only the configured Palworld container after saving.

This temporary integration should migrate into a future plugin when the plugin foundation exists.

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
- `Palworld:ContainerName`
- `Palworld__ContainerName`

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
- Manual lifecycle actions in the details view
- Friendly Docker-unavailable responses
- Tests for contracts, mapping and error handling

Not implemented yet:

- Metrics
- Authentication
- Authorization
- Plugins
- Real-time logs
- SignalR or WebSockets

Read-only endpoints must not alter container state. Lifecycle endpoints are explicit manual actions limited to start, stop and restart.

The temporary Palworld config endpoints are separate from Docker Core:

- `GET /api/palworld/config`
- `PUT /api/palworld/config`
- `PUT /api/palworld/config?restart=true`

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

The Compose deployment uses a GamesHud-owned network created by Compose. It must not use Palworld or Portainer networks.

The frontend is published only on VPS loopback while authentication does not exist. The API is not published to the host by default and is reached by the frontend over the internal Compose network.

### Deployment Security Rules

- Only `gameshud-api` may mount the Docker socket.
- `gameshud-frontend` must never mount the Docker socket.
- The Docker socket must never be exposed over TCP.
- GamesHud must not enable Docker remote API.
- No Docker credentials or host-level secrets may be sent to the frontend.
- No database or persistent volume is introduced until there is real persistent state.

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
