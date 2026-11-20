# Authentication and Managed ownership

SEC-04 establishes the first authenticated, owner-scoped HTTP boundary for the Managed provisioning API.

## Architecture

GamesHud uses local ASP.NET Core Identity with a minimal `ApplicationUser`. The browser session is an Identity application cookie in a same-origin BFF layout: the browser calls `/api` through the frontend origin. The cookie is HttpOnly, `SameSite=Strict`, path `/`, and `Secure` outside Development. API challenges and access denials return `401` and `403`; they never redirect to an HTML login page.

There is no bearer token, external identity provider, public registration route, default account, seeded password, or production authentication bypass. `IAccountProvisioningService` is an internal application boundary for a future controlled tool. SEC-04 does not provide an operational first-account mechanism.

Login failures always return `invalid_credentials`. Identity supplies password hashing and validation and locks a user for five minutes after five failed password attempts. Auth responses contain only the stable user id and email.

## SPA antiforgery flow

Cookie-authenticated mutations use ASP.NET Core antiforgery:

1. Call `GET /api/auth/csrf`. The framework stores its antiforgery cookie and returns the paired request token as JSON.
2. Send that request token in `X-CSRF-TOKEN` on `POST /api/auth/login`, `POST /api/auth/logout`, and `POST /api/game-servers`.
3. Continue sending cookies through the same origin.

Login is protected because accepting an attacker-controlled login can bind a browser to the attacker's session. The authentication cookie remains HttpOnly. The antiforgery cookie is separate and the request token is not an authentication credential.

## Ownership

`ICurrentUser` reads the trusted authenticated principal. Managed application services do not read `HttpContext` directly. New HTTP-created Managed GameServers and their idempotency ledgers receive the same non-null `OwnerId` in the transaction that creates the server, operation, steps, reservations, configuration, image intent, and ledger.

Public request DTOs contain no owner, user, or account field. Managed reads include `OwnerId` in the database query. Cross-owner ids return `404`, which avoids confirming that another user's resource exists. Historical rows remain `OwnerId = NULL` and are invisible through the authenticated Managed API. Internal worker and recovery paths continue to load durable work by server or operation id without retaining an HTTP context.

Idempotency is unique by `(OwnerId, IdempotencyKeyHash)`. A partial unique index preserves the former global namespace for historical ledgers whose owner is null. Only deterministic SHA-256 hashes of the opaque key and the explicit versioned canonical request are persisted.

Owner foreign keys use `ON DELETE RESTRICT`; deleting an account cannot cascade-delete servers or ledgers. Account deletion is outside SEC-04.

## Data Protection and deployment gates

Identity cookies depend on stable ASP.NET Core Data Protection keys. `Authentication:DataProtectionKeysPath` selects a backend-controlled persistent directory. When it is empty, GamesHud uses `system/data-protection-keys` under the resolved backend `Storage:DataRoot`. Production must mount that location on persistent storage shared by every instance using the same cookie application name. Key material must never be stored in the repository or logged.

HTTPS is required before public exposure because production cookies are always Secure. Deployment wiring was intentionally left unchanged in SEC-04. SEC-06 must activate and verify persistent Data Protection storage, HTTPS, rate limiting, security headers, and a controlled operational account-provisioning flow.

SEC-04 protects only `POST /api/game-servers` and the two Managed reads. The legacy administrative surface remains anonymous and powerful. It must not be publicly exposed in a multi-user deployment. SEC-05 must isolate container lifecycle, logs, backups, Palworld administration, scheduler, update, persistence, and system operations, and add an actor-aware audit trail.

GH-16B remains opt-in. Its real Docker validation must pass before Managed provisioning is activated outside isolated development, and the default test suite does not execute Docker.
