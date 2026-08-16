# GamesHud Roadmap

Versions and scope may evolve, but the architectural order should be preserved: generic Docker Core first, operational safeguards next, private deployment foundation, then authentication, public exposure and plugins.

---

## Status Legend

- Completed
- In progress
- Planned
- Deferred

---

## v0.1 - Foundation

Status: Completed

- .NET 8 API
- React + Vite + TypeScript
- xUnit tests
- Repository documentation
- Health endpoint
- Base architecture rules

---

## v0.2 - Container List

Status: Completed

- Docker Engine integration
- `GET /api/containers`
- Container list in frontend
- Loading, empty and error states
- Configurable Docker endpoint
- Configurable frontend API URL

---

## v0.3 - Container Details and Logs

Status: Completed

- `GET /api/containers/{containerId}`
- `GET /api/containers/{containerId}/logs`
- Container details
- Ports
- Mounts
- Networks
- Recent logs snapshot
- Tail validation
- Read-only frontend details view
- No lifecycle actions

---

## v0.4 - Container Lifecycle

Status: Completed

- Start
- Stop
- Restart
- Confirmation dialogs
- Duplicate action prevention
- User feedback
- No automatic operations
- Operational safeguards for active services
- Backend lifecycle endpoints
- Details-view-only frontend controls
- Stop and restart timeout validation

---

## v0.5 - Deployment Foundation

Status: In progress

- GamesHud API Dockerfile
- GamesHud frontend Dockerfile
- nginx static frontend runtime
- Docker Compose file
- Example deployment environment file
- Private loopback publishing while authentication does not exist
- API-only Docker socket mount
- Compose-owned GamesHud network
- Deployment documentation
- Pending Docker build validation
- Pending VPS homologation

---

## v0.6 - Metrics

Status: Completed

- Host CPU
- Host RAM
- Disk usage
- Container CPU and memory
- Running and stopped container summaries
- Palworld CPU, RAM, uptime and players online
- Short in-memory metrics history
- Dashboard metric cards
- Palworld Overview resource charts

---

## v0.7 - Temporary Palworld Backups

Status: Completed

- Manual backup creation
- Palworld REST world save before backup when available
- Backup history
- Backup metadata and download endpoint
- Strong-confirmation restore flow
- Automatic pre-restore backup
- Delete backup
- Internal automatic backup scheduler
- Automatic backup retention
- Palworld Backups frontend page
- Backend tests for backup, restore, retention and path safety

Temporary note:

- This is not the plugin system.
- It should migrate into the future Palworld plugin after v0.10.

---

## v0.8 - Authentication and Authorization

Status: Planned

- Secure login
- Session management
- Administrator role
- Protection for Docker lifecycle actions

---

## v0.9 - Public Deployment / HTTPS

Status: Planned

- Public exposure only after authentication
- Reverse proxy decision
- HTTPS configuration
- Domain configuration
- Production deployment hardening

---

## v0.10 - Plugin Foundation

Status: Planned

- Minimal plugin contract
- Generic Docker integration
- No speculative full SDK

---

## v0.11 - Palworld Plugin

Status: Planned

- Server status
- Players online
- REST API
- RCON
- Configuration editing
- Controlled restart
- Update workflow
- Backup and restore

Temporary note:

- A personal Palworld settings editor exists before the plugin foundation.
- A personal Palworld REST overview and players view exists before the plugin foundation.
- A personal Palworld backup manager exists before the plugin foundation.
- It is not the plugin system and should be migrated after v0.10.

---

## Deferred

- Real-time logs
- SignalR/WebSockets
- Scheduler
- Multi-user permissions
- Additional game plugins
