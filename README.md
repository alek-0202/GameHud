# GamesHud

GamesHud is a web platform for managing Docker containers, game servers and infrastructure through a generic Docker Core and future optional plugins.

The project is in early development. The current focus is a safe Docker management foundation with explicit manual lifecycle actions and a private Docker Compose deployment foundation for homologation.

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
- Lifecycle safeguards such as confirmation dialogs and duplicate action prevention
- Configurable Docker endpoint
- Configurable frontend API URL
- Local Docker Compose deployment foundation
- Temporary personal Palworld settings editor isolated outside Docker Core
- Temporary Palworld REST overview and players view isolated outside Docker Core

Deployment foundation status:

- Dockerfiles and Compose configuration are present locally.
- VPS deployment has not been claimed as completed.
- Docker/VPS homologation must still be performed before treating deployment as validated.

Planned:

- Metrics
- Authentication and authorization
- Public deployment after authentication
- Plugin foundation
- Game-specific plugins

Not implemented yet:

- Real-time log streaming
- Metrics
- Authentication
- Authorization
- Plugins

Development and deployment strategy:

```text
Local development
-> automated validation
-> Git
-> deployment to VPS
-> controlled integration testing
```

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
- nginx for containerized static hosting

Testing:

- xUnit

Infrastructure:

- Docker
- Docker Compose
- Linux-compatible deployment target

---

## Repository Structure

```text
GamesHud/
|-- backend/
|-- deploy/
|-- docs/
|-- frontend/
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

The API runs locally at:

```text
http://localhost:5258
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

Temporary Palworld settings editor:

- `Palworld__ManagedPath`
- `Palworld__ContainerName`
- `Palworld__ConnectionAddress`
- `Palworld__RestApi__BaseUrl`
- `Palworld__RestApi__Username`
- `Palworld__RestApi__Password`
- `Palworld__RestApi__TimeoutSeconds`

The managed path must point to a directory containing `PalWorldSettings.ini`. The container name is the only container targeted by the Palworld Save & Restart flow. Keep `DISABLE_GENERATE_SETTINGS=true` in the Palworld deployment when GamesHud edits the file directly. REST credentials are used only by the backend. Do not commit real VPS paths, server passwords, REST credentials or private container names.

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

Temporary Palworld config:

```text
GET /api/palworld/config
PUT /api/palworld/config
PUT /api/palworld/config?restart=true
GET /api/palworld/overview
GET /api/palworld/players
```

`GET` returns a typed settings list with metadata, current values and `hasValue` for protected password settings. It does not return plaintext passwords or raw INI content. Empty password values on `PUT` preserve the current password.

The Palworld overview and players endpoints use GamesHud contracts and sanitize REST API data before returning it to the frontend.

---

## Docker Compose Deployment

The deployment foundation lives in:

```text
deploy/
|-- compose.yaml
`-- .env.example
```

Container images:

- `gameshud-api:local`
- `gameshud-frontend:local`

Internal ports:

- API: `8080`
- Frontend nginx: `80`

Suggested loopback publication:

```text
127.0.0.1:8088:80
```

The API is not published to the VPS host by default. The frontend proxies `/api` and `/health` to the API over the private Compose network.

For the temporary Palworld config editor, only `gameshud-api` receives the configured Palworld data directory mount. The frontend does not receive the Palworld mount or Docker socket.

Manual commands:

```bash
cd deploy
docker compose build
docker compose up -d
docker compose ps
docker compose logs
```

For updates:

```bash
docker compose up -d --build
```

Do not run remote deploy automatically from development tasks. See [Deployment Guide](docs/deployment.md) for private deployment and homologation details.

## Private Access

Authentication does not exist yet, so GamesHud must not be exposed publicly.

Access the VPS deployment through SSH port forwarding:

```bash
ssh -L 8088:127.0.0.1:8088 user@your-vps-host
```

Then open:

```text
http://localhost:8088
```

Do not commit real VPS hosts, IPs, users, keys, tokens, passwords or private credentials.

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

- Only the backend API may access the Docker socket.
- The frontend must never access the Docker socket.
- The Docker socket must never be exposed over TCP.
- GamesHud must not enable Docker remote API.
- Docker credentials must never be sent to the frontend.

The VPS may already host production containers such as Palworld and Portainer. GamesHud development, deployment, and testing must not stop, restart, recreate, or modify those services except through the explicit temporary Palworld Save & Restart feature, which targets only the configured Palworld container.

## Documentation

- [Architecture](ARCHITECTURE.md)
- [AI Rules](AI_RULES.md)
- [Roadmap](ROADMAP.md)
- [Development Guide](docs/development.md)
- [Deployment Guide](docs/deployment.md)
- [Palworld Settings Guide](docs/palworld-settings.md)
- [Palworld REST API Guide](docs/palworld-rest-api.md)
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
