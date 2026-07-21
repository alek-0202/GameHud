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
- Docker, when testing Docker-backed endpoints locally

Do not install SDKs, runtimes or global tools automatically as part of routine development tasks. If a missing tool blocks work, report it clearly.

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

For local development, leave the endpoint empty when the Docker client default is correct for the machine. Use local environment variables for machine-specific values and do not commit them.

Docker may be unavailable during local development. In that case:

- `GET /health` should still work.
- Docker-dependent endpoints should return friendly service-unavailable responses.
- Stack traces and sensitive details should not be returned.

The Docker socket is high privilege. Only the backend should access Docker Engine.

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
GET http://localhost:5258/api/containers/{containerId}/logs?tail=500&timestamps=true
```

Log query parameters:

- `tail`: recent line count. Default `200`, minimum `1`, maximum `2000`.
- `timestamps`: include Docker timestamps. Default `true`.

Logs are returned as a snapshot response and are not streamed.

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
