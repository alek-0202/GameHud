# GamesHud

GamesHud is a web platform for managing Docker containers, game servers and infrastructure through a generic Docker Core and future optional plugins.

The project is in early development. The current focus is a safe Docker management foundation with explicit manual lifecycle actions.

---

## Current Status

Implemented:

- .NET 8 backend API
- React + Vite + TypeScript frontend
- xUnit test project
- Repository documentation and architecture rules
- `GET /health`
- `GET /api/containers`
- `GET /api/containers/{containerId}`
- `GET /api/containers/{containerId}/logs`
- `POST /api/containers/{containerId}/start`
- `POST /api/containers/{containerId}/stop`
- `POST /api/containers/{containerId}/restart`
- Frontend container list
- Frontend container details view
- Recent logs snapshot view
- Manual frontend start, stop and restart actions in the details view
- Configurable Docker endpoint
- Configurable frontend API URL

Planned:

- Metrics
- Deployment foundation
- Authentication and authorization
- Plugin foundation
- Game-specific plugins

Not implemented yet:

- Real-time log streaming
- Metrics
- Authentication
- Authorization
- Plugins

---

## Technology Stack

Backend:

- .NET 8
- ASP.NET Core Web API
- Docker.DotNet

Frontend:

- React
- Vite
- TypeScript

Testing:

- xUnit

Infrastructure:

- Docker
- Linux-compatible deployment target

---

## Repository Structure

```text
GamesHud/
|-- backend/
|-- frontend/
|-- docs/
|-- AI_RULES.md
|-- ARCHITECTURE.md
|-- README.md
`-- ROADMAP.md
```

The repository may live in any directory. Commands and documentation should use paths relative to the repository root.

---

## Quick Start

Backend:

```bash
cd backend
dotnet restore
dotnet build
dotnet test
dotnet run --project src/GamesHud.Api/GamesHud.Api.csproj
```

Frontend:

```bash
cd frontend
npm install
npm run dev
```

Build frontend:

```bash
cd frontend
npm run build
```

---

## Configuration

Backend Docker endpoint:

- `Docker__Endpoint`

When this value is empty or unset, Docker.DotNet uses its default local Docker client behavior. Use environment-specific values locally or in deployment, but do not commit personal paths, production endpoints, credentials or local secret files.

Frontend API base URL:

- `VITE_API_BASE_URL`

For local development, create `frontend/.env` from `frontend/.env.example` and set the API base URL, for example:

```text
VITE_API_BASE_URL=http://localhost:5258
```

Local `.env` files are ignored by git and must not be committed.

---

## API

Health:

```text
GET /health
```

Containers:

```text
GET /api/containers
```

Container details:

```text
GET /api/containers/{containerId}
```

`containerId` may be a full container ID, abbreviated ID or container name when Docker Engine accepts it.

Recent logs:

```text
GET /api/containers/{containerId}/logs
GET /api/containers/{containerId}/logs?tail=200&timestamps=true
```

Log query parameters:

- `tail`: recent line count. Default `200`, minimum `1`, maximum `2000`.
- `timestamps`: include Docker timestamps. Default `true`.

Logs are returned as a bounded snapshot, not as a stream.

Lifecycle actions:

```text
POST /api/containers/{containerId}/start
POST /api/containers/{containerId}/stop
POST /api/containers/{containerId}/stop?timeoutSeconds=10
POST /api/containers/{containerId}/restart
POST /api/containers/{containerId}/restart?timeoutSeconds=10
```

Lifecycle actions are explicit manual operations only. `timeoutSeconds` applies to stop and restart, defaults to `10`, and must be between `1` and `120`.

If Docker is unavailable, `/health` should still work and Docker-dependent endpoints return friendly service-unavailable responses without stack traces.

---

## Security Notes

Access to the Docker socket is high privilege because it can allow control over the host.
Only the backend may access Docker Engine.
The frontend must call the GamesHud API and must never access Docker directly.

Public API contracts must not expose:

- Container environment variables
- Secrets
- Tokens
- Private keys
- Raw Docker inspect payloads
- Docker socket details
- Production endpoints

Mount sources can reveal host paths and should be treated as operationally sensitive information for a future authenticated admin panel.

Lifecycle actions are limited to start, stop and restart. There are no batch actions, automatic scheduled actions, remove, kill, exec, recreate, rename or update operations.

---

## Documentation

- [Architecture](ARCHITECTURE.md)
- [AI Rules](AI_RULES.md)
- [Roadmap](ROADMAP.md)
- [Development Guide](docs/development.md)
- [API Guidelines](docs/api-guidelines.md)
- [Frontend Guidelines](docs/frontend-guidelines.md)

---

## Design Principles

- Generic Docker Core
- Plugin-ready architecture
- Clear separation of responsibilities
- Production-oriented development
- Simplicity before abstraction

---

## License

Not defined yet.
