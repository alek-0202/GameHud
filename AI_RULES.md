# AI Development Rules

This document defines how AI assistants must work within the GamesHud repository.

These rules apply to implementation, refactoring, bug fixes, documentation changes, reviews and architectural analysis.

---

## 1. Required Context Review

Before modifying files, the AI must read and understand:

- `README.md`
- `ARCHITECTURE.md`
- `AI_RULES.md`
- `docs/development.md`
- `.editorconfig`
- `.gitignore`
- Any guideline or documentation directly related to the requested task

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

Do not introduce a database unless the feature requires persistent state.

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

Documentation-only tasks do not require build and test when no functional code or runtime configuration was changed. They still require link, path and consistency checks.

If validation cannot be completed, report the exact blocker and avoid claiming success.
