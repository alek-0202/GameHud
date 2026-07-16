# Development

## Prerequisites

- .NET 8 SDK
- Node.js
- npm

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

Health check:

```text
GET http://localhost:5258/health
```

Containers:

```text
GET http://localhost:5258/api/containers
```

The endpoint lists all Docker containers, including stopped containers, and returns:

- `id`
- `name`
- `image`
- `state`
- `status`

Docker access is configured in `appsettings.json`:

```json
{
  "Docker": {
    "Endpoint": ""
  }
}
```

You can override it with:

```powershell
$env:Docker__Endpoint = "unix:///var/run/docker.sock"
```

For local development, Docker may be unavailable or inaccessible.
In that case, `/health` should still work, while `/api/containers` returns a friendly service-unavailable response.
The planned Linux VPS endpoint is `unix:///var/run/docker.sock`.
Do not commit production Docker endpoints, VPS addresses, certificates, credentials, or real local secrets.

The Docker socket gives broad control over the host.
Only the backend should access it; the frontend must call the GamesHud API instead.

## Frontend

Install dependencies:

```powershell
cd frontend
npm install
```

Build:

```powershell
cd frontend
npm run build
```

Run:

```powershell
cd frontend
npm run dev
```

Configure the API base URL with `frontend/.env.example`:

```text
VITE_API_BASE_URL=http://localhost:5258
```

Create a local `.env` if needed, but do not commit real `.env` files.

## Repository Conventions

- Use English for code, file names, class names, method names, variables, and technical folders.
- Keep Docker Core behavior independent from game-specific behavior.
- Do not version sensitive configuration, secrets, tokens, or local environment files.
- Prefer simple implementations until real requirements justify abstractions.
- Keep backend and frontend responsibilities clearly separated.
