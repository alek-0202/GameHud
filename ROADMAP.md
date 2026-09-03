# GamesHud Roadmap

Versions and scope may evolve, but the architectural order should be preserved: generic Docker Core first, operational safeguards next, private deployment foundation, then authentication, public exposure and plugins.

Security milestone details are tracked in [Threat Model](docs/security/threat-model.md).

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
- It should migrate into the future Palworld plugin after v0.12.

---

## v0.8 - Temporary Palworld Updates

Status: Completed

- Installed version display
- Steam build update status check
- Manual update apply flow
- Strong-confirmation update modal
- Update announcement when REST is available
- Required world save before update
- Required pre-update backup
- Configured Palworld container stop/start only
- Health check after update
- No automatic updates
- No binary rollback promise
- Backend tests for status, exact order and failure steps

Temporary note:

- This is not the plugin system.
- It should migrate into the future Palworld plugin after v0.12.
- Docker/VPS homologation against the real Palworld image is still required.

---

## v0.9 - Advanced Operations

Status: Completed

- Improved bounded log snapshots
- Log search and term filtering
- stdout/stderr filtering when Docker provides stream metadata
- Optional timestamps
- Optional frontend auto refresh and pause
- Frontend log snapshot download
- Visual log severity highlights
- In-memory scheduler for supported safe actions
- Scheduled automatic backup
- Scheduled Palworld restart flow
- Scheduled update check
- Scheduled announcement
- Scheduled Palworld shutdown
- Optional backend-only Discord webhook notifications
- Notification cooldown and deduplication
- Settings UI for notifications and scheduler
- Backend tests for schedule, recurrence, disabled tasks, duplicate execution, webhook failure, webhook secrecy, cooldown and cancellation

Temporary note:

- Scheduler state is in API memory and should be revisited when durable authenticated administration exists.
- Discord webhook values are configuration-only secrets and must not be sent to the frontend.

---

## v0.10 - Authentication and Authorization

Status: Planned

- Secure login
- Session management
- Administrator role
- Protection for Docker lifecycle actions
- SEC-01 threat model (Completed)
- SEC-02 secrets management and redaction (Completed)
- SEC-03 authentication (Planned)
- SEC-04 ownership and authorization (Planned)
- SEC-05 audit and security logging (Planned)
- SEC-06 API and deployment hardening (Planned)

---

## v0.11 - Public Deployment / HTTPS

Status: Planned

- Public exposure only after authentication
- Reverse proxy decision
- HTTPS configuration
- Domain configuration
- Production deployment hardening

---

## v0.12 - Plugin Foundation

Status: In progress

- Minimal plugin contract
- Generic Docker integration
- No speculative full SDK
- Game server registry with legacy Palworld fallback
- `GET /api/servers`
- `GET /api/servers/{serverId}`
- Server capabilities for overview, settings, players, backups, update and logs
- Frontend Game Servers page backed by configured servers
- GH-01 minimal game server domain model (Completed)
- Explicit game server id, extensible game id, runtime and installation ownership
- Legacy/external projection for the existing `Servers` collection and singular Palworld fallback
- Deterministic duplicate server id rejection
- GH-02 game definition system (Completed)
- Code-backed Palworld definition with static metadata, Docker runtime support and implemented capabilities
- Separate game definition registry with deterministic duplicate and unknown-id behavior
- GH-03 game catalog (Completed)
- `GET /api/games` and `GET /api/games/{gameId}` backed by `GameDefinitionRegistry`
- Frontend Game Catalog page separated from configured game servers
- GH-04 host capability detection (Completed)
- `GET /api/system/capabilities` for read-only OS, CPU, memory, storage, network and Docker runtime inspection
- Host capabilities are separate from game requirements and do not perform provisioning
- GH-05 game requirements engine (Completed)
- Palworld requirements declared from the official server guide
- `GET /api/games/{gameId}/compatibility` combines static game requirements with read-only host capabilities
- Frontend Game Catalog can check host fit on demand without creating or provisioning servers
- GH-06 port management foundation (Completed)
- Game definitions can declare logical ports with protocol, exposure and alternative policy
- Palworld declares game, query and private REST/admin ports
- `GET /api/system/ports/{protocol}/{port}` checks host port availability without mutation
- `POST /api/games/{gameId}/ports/plan` previews preferred and alternative port candidates
- Port availability remains advisory until durable reservation exists
- GH-07 storage and volume manager foundation (Completed)
- Game definitions can declare logical storage entries with purpose, runtime target, persistence and backup eligibility
- Palworld declares persistent game data at runtime target `/palworld` without a host path
- `Storage__DataRoot` configures the GamesHud managed data root with an app-local fallback
- `POST /api/games/{gameId}/storage/plan` previews deterministic managed layout for a supplied `gameServerId`
- Storage planning is advisory and does not create directories, Docker volumes, bind mounts, compose changes or durable records
- GH-07.5 persistence foundation (Completed)
- EF Core + SQLite stores technical persistence metadata under `<DataRoot>/system/gameshud.db`
- EF migrations are versioned and startup initialization is centralized behind a persistence initializer
- `GET /api/system/persistence` reports provider and migration health without exposing paths or connection strings
- The foundation does not persist secrets, register game servers, reserve ports/storage or adopt legacy Palworld resources
- GH-07.6 product database schema (Completed)
- Managed game server records, port reservations, storage reservations and provisioning operation records are persisted
- Port reservations enforce durable `protocol + port` uniqueness while allowing TCP/UDP to share a number
- Storage reservations enforce unique managed relative paths under the GamesHud data root
- Reservation writes are atomic and remain separate from Docker, filesystem and provisioning mutation
- GH-08 provisioning engine foundation (Completed)
- Internal request-to-validated-plan-to-reservation pipeline reuses host requirements and port/storage planners
- Typed steps persist operation progress, safe failure, cancellation and terminal state
- Compensation and incomplete-operation query foundations exist without automatic startup recovery
- All production host-facing steps are explicit no-mutation implementations; no server runtime is created
- GH-09 provisioning state machine (Completed)
- Operation and individual step transitions are centralized, persisted and exhaustively tested
- Versioned pipelines preserve stable step order, attempts, retry policy and side-effect classification across restart
- Startup classifies incomplete operations without automatically retrying mutation or executing recovery
- Reconciliation and durable reverse-compensation contracts exist with optimistic checkpoint concurrency
- SEC-02 secrets management foundation (Completed)
- Provider-neutral secret ids, references, purposes and backend-only secret values exist before provisioning
- The local provider stores authenticated encrypted material under `<DataRoot>/system/secrets`
- The bootstrap key is external configuration only, and missing/invalid keys fail closed without plaintext fallback
- `GET /api/system/secrets` reports minimal readiness without exposing ids, purposes, counts, paths or material
- SEC-03 runtime mutation security policy (Completed)
- Typed unvalidated/validated runtime specifications and deny-by-default policy
- Backend-controlled runtime images, managed reservation mount/port enforcement and opaque secret references
- Typed no-mutation runtime adapter boundary integrated before `create_runtime`
- GH-10 runtime mutation execution foundation (Completed)
- Stable mutation/resource identities, typed provider outcomes and safe exception/cancellation translation
- Unknown runtime outcomes route to GH-09 reconciliation without blind retry
- GH-11 managed storage preparation (Completed)
- Typed Managed targets, DataRoot containment and symlink/reparse inspection before directory creation
- Idempotent directory preparation and real storage reconciliation without destructive compensation

Remaining:

- Durable server registration UI
- Non-Palworld plugin implementation
- Migration of all temporary Palworld features onto server-scoped contracts
- GH-12 Managed Docker runtime creation (Completed: create-only, stopped, reconciliable)
- GH-13 Managed runtime start and generic readiness (Completed: typed start, reconciliation, bounded read-only health)

---

## v0.13 - Palworld Plugin

Status: In progress

- Server status
- Players online
- REST API
- Configuration editing
- Controlled restart
- Update workflow
- Backup and restore
- Announce
- Kick
- Ban
- Unban
- Strong confirmation for destructive player actions
- Local mod inventory only

Temporary note:

- The Palworld plugin foundation is incremental and still reuses some temporary Palworld services.
- Backups, updates, metrics and scheduler flows still need full server-scoped migration before multiple Palworld servers are considered complete.
- Mod management is inventory-only until the current Linux container environment has a safe, predictable install and enable/disable mechanism.

---

## Operations Foundation

Status: In progress

- OPS-01 CI pipeline (Completed)
- GitHub Actions validation for pushes and pull requests on `develop` and `main`
- Separate backend build/test, frontend build and EF Core migration checks
- Read-only workflow permissions and isolated tests without production secrets, Docker, Palworld or VPS access
- Documented local CI equivalents and future branch protection check names
- OPS-02 dependency and secret scanning (Completed)
- High/Critical gates for direct and transitive NuGet and npm advisories
- Weekly full-history secret scanning with pinned, checksum-verified Gitleaks
- Zero known npm and NuGet vulnerability findings at the OPS-02 baseline
- Weekly Dependabot PRs for NuGet, npm and GitHub Actions without auto-merge
- OPS-03 release versioning and integrity (Planned)
- OPS-04 backup and restore hardening (Planned)

---

## Deferred

- Real-time logs
- SignalR/WebSockets
- Multi-user permissions
- Additional game plugins
- ARCH-01 GamesHud Agent architecture
