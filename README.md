# GamesHud

GamesHud is a modern web platform for managing Docker containers, game servers and infrastructure through a clean and extensible interface.

The project focuses on providing a generic Docker management core while allowing specialized features through independent plugins.

---

## Current Status

🚧 Early development

Current implementation:

- Project foundation
- Backend (.NET 8)
- Frontend (React + Vite)
- Test project
- Documentation
- Docker container listing through the Docker Engine API

Planned:

- Dashboard
- Container management
- Logs
- Metrics
- Plugin system
- Game server administration

---

## Technology Stack

Backend

- .NET 8
- ASP.NET Core Web API
- Docker.DotNet

Frontend

- React
- Vite
- TypeScript

Testing

- xUnit

Infrastructure

- Docker
- Linux

---

## Repository Structure

```text
GamesHud/
├── backend/
├── frontend/
├── docs/
├── ARCHITECTURE.md
└── README.md
```

---

## Local Development

Backend

```bash
cd backend

dotnet restore
dotnet build
dotnet test

dotnet run --project src/GamesHud.Api
```

The Docker connection is configured through:

```json
{
  "Docker": {
    "Endpoint": ""
  }
}
```

The endpoint can be overwritten with the `Docker__Endpoint` environment variable.
When the value is empty, the backend uses the Docker client default for the local machine.
For future Linux VPS usage, the expected value is:

```text
unix:///var/run/docker.sock
```

Frontend

```bash
cd frontend

npm install
npm run dev
```

Create a local `.env` from `frontend/.env.example` and set:

```text
VITE_API_BASE_URL=http://localhost:5258
```

Build

```bash
npm run build
```

## API

Health:

```text
GET /health
```

Containers:

```text
GET /api/containers
```

The containers endpoint returns all containers, including stopped containers, with:

- `id`
- `name`
- `image`
- `state`
- `status`

If Docker is unavailable, the API returns a friendly error response without exposing stack traces.

## Security Notes

Access to the Docker socket is highly sensitive because it can allow control over the host.
Only the backend should access the Docker Engine endpoint.
The frontend must communicate with the backend API and must never receive Docker socket paths, certificates, credentials, or production endpoints.

---

## Documentation

Project architecture:

- ARCHITECTURE.md

Development guide:

- docs/development.md

---

## Design Principles

GamesHud follows a few core principles:

- Generic Docker Core
- Plugin-based architecture
- Clear separation of responsibilities
- Production-oriented development
- Simplicity before abstraction

---

## License

Not defined yet.
