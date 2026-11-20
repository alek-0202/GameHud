# Continuous Integration

GamesHud uses GitHub Actions to validate changes before integration. The CI workflow is source-only: it does not deploy, connect to a VPS, use the Docker socket, provision game servers or depend on Palworld.

## Triggers

The CI and security workflows run for:

- pull requests targeting `develop` or `main`;
- pushes to `develop` or `main`.

Older runs are cancelled only for the same pull request. Push runs, including runs on `main`, are not cancelled by the workflow concurrency policy.

The security workflow also runs every Monday at 05:00 UTC so newly published advisories are detected without requiring a source change.

## Checks

### Backend Build & Tests

The backend check uses the .NET 8 SDK and the real solution at `backend/GamesHud.sln`. It restores dependencies, builds in Release configuration and runs the complete test suite without rebuilding.

The repository does not currently contain a `global.json`. CI therefore selects the latest available .NET 8 SDK patch through `8.0.x`, matching the `net8.0` projects and .NET 8 Docker images without introducing a machine-derived SDK pin.

### Frontend Build

The frontend check uses Node.js 22, matching `frontend/Dockerfile`. Dependencies are installed with `npm ci` from `frontend/package-lock.json`; CI never runs `npm install` or modifies the lockfile. The official `setup-node` npm cache is enabled using that lockfile.

`npm run build` executes `tsc -b` and the Vite production build. There are no separate `lint` or `typecheck` scripts in `frontend/package.json`, so CI does not run or invent those checks.

### Persistence Migration Check

The persistence check restores the repository-local `dotnet-ef` tool from `.config/dotnet-tools.json`, restores and builds the API project, then lists migrations using the real project, startup project and `GamesHudDbContext`.

The backend test suite separately applies migrations to isolated temporary SQLite databases. CI does not use a developer or production `DataRoot`, and it never touches a real GamesHud database.

### Dependency Security Scan

The dependency check scans direct and transitive NuGet packages with the official .NET SDK report and audits the committed npm lockfile. High and Critical advisories fail the check; Moderate and Low advisories are reported.

The .NET gate is implemented by `scripts/security/Test-NuGetVulnerabilities.ps1`, which consumes structured scanner output rather than matching human-readable text. `npm audit --audit-level=high` provides the npm gate.

### Secret Scan

The secret check scans full Git history with Gitleaks `8.30.1`. CI verifies the pinned release archive SHA-256 before execution and fully redacts any detected value. No secret report artifact is published.

See [Dependency And Secret Security](security/dependency-security.md) for advisories, baseline, failure behavior and update policy.

## Security And Isolation

The workflow grants the GitHub token only `contents: read`. Official GitHub actions are pinned to immutable release commit SHAs, with the release version retained in comments for review and Dependabot updates. The workflows do not publish artifacts.

CI does not configure `Secrets__MasterKey` and does not require a production key. Secret-store tests generate test-only ephemeral keys at runtime. The workflow must never contain a production master key, VPS key, environment dump, production connection string or other operational credential.

The checks do not require Docker, a Docker daemon, the Docker socket, Palworld, a game server, external services, VPS access or a production data root.

## Local Equivalent

Run these commands from the repository root before pushing:

```powershell
dotnet restore backend/GamesHud.sln
dotnet build backend/GamesHud.sln --configuration Release --no-restore
dotnet test backend/GamesHud.sln --configuration Release --no-build
./scripts/security/Test-NuGetVulnerabilities.ps1

dotnet tool restore
dotnet restore backend/src/GamesHud.Api/GamesHud.Api.csproj
dotnet build backend/src/GamesHud.Api/GamesHud.Api.csproj --configuration Release --no-restore
dotnet tool run dotnet-ef migrations list --project backend/src/GamesHud.Api/GamesHud.Api.csproj --startup-project backend/src/GamesHud.Api/GamesHud.Api.csproj --context GamesHudDbContext --configuration Release --no-build

Set-Location frontend
npm ci
npm audit --audit-level=high
npm run build
Set-Location ..

git diff --check
git status --short
```

## Branch Protection Readiness

When branch protection is configured manually in GitHub, these checks should be required:

- `Backend Build & Tests`;
- `Frontend Build`;
- `Persistence Migration Check`;
- `Dependency Security Scan`;
- `Secret Scan`.

Repository settings are not changed by this workflow. Final GitHub Actions validation occurs after the workflow is pushed and triggered by GitHub.
