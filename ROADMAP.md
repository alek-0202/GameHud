# GamesHud Roadmap

Versions and scope may evolve, but the architectural order should be preserved: generic Docker Core first, operational safeguards next, then authentication, deployment and plugins.

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

## v0.5 - Metrics

Status: Planned

- Host CPU
- Host RAM
- Disk usage
- Container CPU and memory
- Running and stopped container summaries

---

## v0.6 - Deployment Foundation

Status: Planned

- GamesHud containerization
- VPS deployment
- Reverse proxy decision
- Environment configuration
- Restricted Docker socket access

---

## v0.7 - Authentication and Authorization

Status: Planned

- Secure login
- Session management
- Administrator role
- Protection for Docker lifecycle actions

---

## v0.8 - Plugin Foundation

Status: Planned

- Minimal plugin contract
- Generic Docker integration
- No speculative full SDK

---

## v0.9 - Palworld Plugin

Status: Planned

- Server status
- Players online
- REST API
- RCON
- Configuration editing
- Controlled restart
- Update workflow
- Backup and restore

---

## Deferred

- Real-time logs
- SignalR/WebSockets
- Scheduler
- Multi-user permissions
- Additional game plugins
