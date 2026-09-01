# AI Development Rules

This document defines how AI assistants must work within the GamesHud repository.

These rules apply to implementation, refactoring, bug fixes, documentation changes, reviews and architectural analysis.

---

## 1. Required Context Review

Before modifying files, the AI must read and understand:

- `README.md`
- `ARCHITECTURE.md`
- `AI_RULES.md`
- `ROADMAP.md`
- `docs/development.md`
- `.editorconfig`
- `.gitignore`
- Any guideline or documentation directly related to the requested task

For backend changes, also read `docs/api-guidelines.md`.
For frontend changes, also read `docs/frontend-guidelines.md`.
For deployment changes, also read `docs/deployment.md`.

The AI must inspect the current repository structure and existing implementation before proposing changes.

The current project conventions always take precedence over generic assumptions.

---

## 2. Workspace and Path Rules

GamesHud must be treated as the repository currently open in the workspace.

Any AI must:

- Identify the current repository root before making changes.
- Work only inside the current repository root unless explicitly instructed otherwise.
- Not assume machine-specific absolute paths.
- Not access or modify other clones without an explicit request.
- Use relative paths in commands, instructions and permanent documentation.
- Avoid committing or documenting personal local paths.
- Report real ambiguity when multiple repositories or candidate roots are detected.

Permanent code, shared configuration and documentation must not require a specific local path such as a drive, user profile or machine-specific directory.

Local configuration belongs in ignored files or environment variables.

---

## 2.1 Git Branch and Synchronization Rules

`main` represents production. `develop` is the integration and development branch.

Before an implementation task, the AI must:

- Identify the repository root.
- Inspect the working tree and current branch.
- Stop and report when local uncommitted changes exist.
- Never discard, restore, clean, reset or stash local changes automatically.
- Fetch the remote and update `develop` using fast-forward only.
- Implement on `develop`, unless the user explicitly requests another non-production branch.

The AI must not implement directly on `main`, merge into `main`, commit, push or deploy unless explicitly requested.

---

## 3. Plan Before Implementation

Before changing files, the AI must provide a brief technical plan containing:

- Objective of the change
- Expected implementation approach
- Main files or areas affected
- Relevant architectural considerations
- Important risks or trade-offs

The plan must remain proportional to the task and must not introduce unnecessary complexity.

---

## 4. Architecture Preservation

All changes must respect `ARCHITECTURE.md`.

In particular:

- Docker Core must remain generic.
- Game-specific behavior must not be added to Docker Core.
- Controllers must not depend directly on external Docker SDK types.
- External SDK models must not be returned directly by the API.
- Application configuration must not be hardcoded.
- New abstractions must only be introduced when there is a concrete need.

The AI must not create a complete Clean Architecture, plugin framework, event bus, mediator layer, repository layer or similar abstraction unless the current task genuinely requires it.

---

## 5. Scope Discipline

The AI must modify only what is necessary for the requested task.

It must not:

- Refactor unrelated code.
- Rename unrelated files or symbols.
- Reformat the entire repository.
- Replace working implementations without a clear reason.
- Change shared components without evaluating impact.
- Install unrelated dependencies.
- Create speculative future features.
- Perform broad cleanup outside the task scope.

When a shared component must be changed, the AI must identify the impact.

---

## 6. Naming and Language

All technical names must be written in English, including:

- Files
- Folders
- Classes
- Interfaces
- Methods
- Properties
- Variables
- DTOs
- Configuration sections
- API contracts
- Test names

User-facing text may follow the product language defined for the interface.

Names must be descriptive and consistent with the existing project.

---

## 7. Security and Sensitive Data

Sensitive or environment-specific information must never be committed or hardcoded.

This includes:

- Passwords
- Tokens
- API keys
- Private keys
- Certificates
- VPS addresses
- Personal IP addresses
- Production Docker endpoints
- Connection strings
- Secrets
- Real `.env` files
- Personal local paths

Use:

- Environment variables
- Application configuration
- Secret stores
- Example configuration files without real credentials

The AI must not expose stack traces, internal exceptions, secrets or unnecessary infrastructure details through public API responses.

Permanent security invariants are documented in [Security Invariants](docs/security/security-invariants.md). In particular:

- Client input must never become an arbitrary host filesystem path.
- Planning endpoints must remain read-only and must not provision host, Docker, firewall or durable resources.
- GamesHud must never automatically delete, overwrite, adopt or migrate External resources.
- Normal API responses must never return plaintext secrets, tokens, webhook URLs, private keys or Palworld REST credentials.
- Secret material must never be persisted as normal application data, and secret storage failure must never downgrade to plaintext or in-memory storage.
- Encryption keys and bootstrap secrets must never be hardcoded, committed or stored next to ciphertext as protection.
- Scheduler and future provisioning flows must never accept arbitrary shell, Docker exec, compose or user-defined commands.
- Managed resource deletion must require durable ownership proof and auditability.
- Future runtime adapters may accept only policy-approved validated specifications; they must not expose arbitrary command, argument or generic provider-option APIs.
- Runtime provider exceptions and ambiguous cancellation must be translated to safe unknown outcomes and reconciled before retry.

---

## 8. Dependency Management

Before adding a dependency, the AI must verify that:

- The existing platform does not already provide the required functionality.
- The dependency is actively maintained.
- It is compatible with the project stack.
- It adds enough value to justify inclusion.
- A smaller or native solution would not be more appropriate.

The AI must document every package added and why it was necessary.

Dependencies must not be upgraded broadly unless the task explicitly requires it.
Never use forced upgrades or unreviewed major-version upgrades to resolve dependency vulnerabilities. Identify the advisory, dependency chain, compatible fixed version and breaking risk first.

---

## 9. Backend Rules

For backend changes, follow [API Guidelines](docs/api-guidelines.md).

Core rules:

- Preserve nullable reference types.
- Preserve implicit usings unless there is a strong reason not to.
- Keep controllers thin.
- Keep infrastructure-specific logic outside controllers.
- Use dependency injection consistently.
- Return internal API contracts instead of external SDK models.
- Handle failures explicitly.
- Log meaningful errors with `ILogger`.
- Do not log secrets or sensitive payloads.
- Do not silently swallow exceptions.
- Avoid unnecessary interfaces unless they provide concrete value.
- Keep public API behavior predictable and documented.

Do not introduce a database unless the feature requires persistent state. GH-07.5 introduced the approved EF Core + SQLite persistence foundation under `Storage__DataRoot`; GH-07.6 added managed server, reservation and operation records. Do not expand persistence into Docker provisioning, users, tenants or secrets without a roadmap task.

---

## 10. Frontend Rules

For frontend changes, follow [Frontend Guidelines](docs/frontend-guidelines.md).

Core rules:

- Use TypeScript without `any`.
- Reuse existing patterns and components.
- Keep API communication separate from presentation when appropriate.
- Handle loading, empty, success and error states.
- Avoid adding large UI libraries without prior architectural agreement.
- Keep components focused and readable.
- Avoid premature global state.
- Keep environment-specific URLs outside source code.
- Preserve responsive behavior.
- Do not introduce visual inconsistency without explicit design direction.

Frontend code must not contain production server addresses or sensitive configuration.

---

## 11. Tests

Every implementation must evaluate whether tests are required.

Tests should cover:

- New business or mapping behavior
- API contracts
- Error handling
- Important regressions
- Security-sensitive behavior
- Non-trivial utility logic

Tests must not depend on:

- A real production VPS
- A real game server
- A real Docker environment, unless explicitly defined as an integration test
- External services that make the test suite unreliable

Prefer controlled fakes, mocks or test doubles for unit and API tests.

Do not create meaningless tests that only verify framework behavior.

---

## 12. Documentation

Update documentation when a change affects:

- Setup
- Configuration
- Environment variables
- API endpoints
- Architecture
- Development commands
- Deployment
- Security considerations
- Operational procedures

Documentation must remain consistent with the implementation.

Do not claim that a feature exists if it has not been implemented and validated.

Permanent rules should be centralized in guideline files:

- Backend rules: `docs/api-guidelines.md`
- Frontend rules: `docs/frontend-guidelines.md`
- Deployment operations: `docs/deployment.md`
- Development commands: `docs/development.md`
- Architecture boundaries: `ARCHITECTURE.md`
- Delivery order: `ROADMAP.md`

Future prompts should avoid repeating full rules already documented. A feature prompt should focus on objective, scope, acceptance criteria and validation.

If a prompt conflicts with repository documentation, report the conflict before implementation.

---

## 13. Validation Before Completion

Before considering a task complete, run all validations relevant to the changed area.

Backend:

```bash
dotnet restore
dotnet build
dotnet test
```

Frontend:

```bash
npm run build
```

Docker and deployment configuration:

```bash
docker compose config
docker compose build
```

Run Docker validation only when Docker is available and it is safe to build images. Do not install Docker, reconfigure Docker, connect to the VPS, or deploy remotely as part of validation unless explicitly requested.

Documentation-only tasks do not require build and test when no functional code or runtime configuration was changed. They still require link, path and consistency checks.

If validation cannot be completed, report the exact blocker and avoid claiming success.

---

## 14. VPS Deployment Safety

GamesHud may be deployed to a real Ubuntu VPS for homologation. The VPS is an execution environment, not a code editing environment.

Development flow:

```text
Local development
-> automated validation
-> Git
-> deployment to VPS
-> controlled integration testing
```

Do not implement changes directly on the VPS.

## 15. Protected Production Containers

The VPS may already run production containers such as Palworld and Portainer.

No GamesHud task may:

- Stop, restart, recreate, or modify Palworld outside the explicit temporary Palworld config flow.
- Run `docker compose down` in the Palworld project.
- Restart the Docker daemon.
- Restart or shut down the VPS.
- Modify Palworld volumes, networks, or configuration outside the configured Palworld settings file.
- Use Palworld as a lifecycle test container.
- Use Portainer for destructive tests.

Lifecycle testing must use a disposable container created specifically for GamesHud homologation.

The temporary Palworld config flow may edit only `PalWorldSettings.ini` under the configured managed path and may stop/start only the configured Palworld container when the user explicitly chooses Save & Restart. It must not use `docker compose down`, kill, remove, recreate, target other containers, or touch Palworld/Portainer networks.

## 16. Deployment Foundation Rules

GamesHud deployment must use Docker Compose.

Backend:

- Use a multi-stage .NET Docker build.
- Keep the runtime image limited to what is needed to run the API.
- Configure through environment variables.
- Mount the Docker socket only in the API service.

Frontend:

- Build React/Vite assets in a multi-stage Docker build.
- Serve static build output through nginx or an equivalent simple static server.
- Never mount or receive access to the Docker socket.

Compose:

- Publish services only on loopback while authentication does not exist.
- Prefer a Compose-owned GamesHud network.
- Do not use Portainer networks or unrelated Palworld project networks.
- The approved temporary Palworld REST integration uses the external `gameshud-palworld` network on `gameshud-api` only.
- Do not create new persistent deployment volumes until required by real state. The GH-07.5 SQLite database is backend-only GamesHud system state under `Storage__DataRoot` and must not be stored in Palworld data or backup mounts.
- Prefer `docker compose up -d --build` for updates.
- Do not run `docker compose down` unnecessarily.

## 17. Private Access Rules

Until authentication exists:

- GamesHud must not be published publicly.
- Do not configure public HTTPS.
- Do not configure domains.
- Do not configure Cloudflare.
- Do not configure a public reverse proxy.
- Access should use SSH port forwarding.

## 18. Chat Commands

The chat command `/push` means:

- Inspect `git status -sb` and the current diff.
- Confirm the working tree changes are related to the current requested scope.
- Run validations relevant to the changed area when practical.
- Stage the intended changes.
- Create a concise commit.
- Push the local `main` branch to `origin/main`.
- Do not create a pull request unless explicitly requested.

If unrelated changes, failing validations, missing credentials, or remote push errors are detected, stop and report the blocker instead of forcing the push.
