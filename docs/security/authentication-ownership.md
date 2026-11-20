# Authentication and Managed ownership

SEC-04 establishes the first authenticated, owner-scoped HTTP boundary for the Managed provisioning API.

## Architecture

GamesHud uses local ASP.NET Core Identity with a minimal `ApplicationUser`. The browser session is an Identity application cookie in a same-origin BFF layout: the browser calls `/api` through the frontend origin. The cookie is HttpOnly, `SameSite=Strict`, path `/`, and `Secure` outside Development. API challenges and access denials return `401` and `403`; they never redirect to an HTML login page.

There is no bearer token, external identity provider, public registration route, default account, seeded password, or production authentication bypass. `IAccountProvisioningService` remains an internal application boundary. SEC-04A adds a separate one-time administrative CLI for the first account only.

Login failures always return `invalid_credentials`. Identity supplies password hashing and validation and locks a user for five minutes after five failed password attempts. Auth responses contain only the stable user id and email.

## First-account bootstrap

The production API image includes `GamesHud.Bootstrap.dll` under `/app/tools/bootstrap`. It uses the same dependency-injection registration, `UserManager<ApplicationUser>`, Identity options, persistence layout and `GamesHudDbContext` as the API. It adds no HTTP route and accepts no command-line arguments, environment variable, file or redirected standard input for credentials.

Run the tool only from an interactive terminal after the API has started successfully and applied its existing migrations:

```bash
cd deploy
docker compose exec -it gameshud-api \
  dotnet /app/tools/bootstrap/GamesHud.Bootstrap.dll
```

The tool prompts for the email, which is also stored as the Identity username, followed by password and confirmation. Password input is not echoed. Do not place credentials after the command, in `.env`, in a shell variable, in a pipe or in a redirected file; the tool rejects command-line arguments and redirected input.

Creation begins a serializable SQLite transaction before checking `AspNetUsers`. This makes competing bootstrap processes serialize against the same persisted database. The first valid attempt creates one account through Identity; every later attempt is refused when any user exists. Invalid Identity credentials leave the user table empty and may be retried interactively.

Exit codes are:

- `0`: first account created;
- `1`: safe unexpected failure;
- `2`: bootstrap refused because an account already exists;
- `3`: invalid input or password confirmation mismatch;
- `4`: configured Identity validation rejected the credentials;
- `64`: non-interactive input or unsupported arguments;
- `130`: cancellation.

Messages never include the password, hash, token, connection string or exception detail. The CLI does not modify migrations, Data Protection keys, Managed ownership or provisioning contracts.

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

HTTPS is required before public exposure because production cookies are always Secure. SEC-04A provides only first-account bootstrap; it does not complete SEC-06 deployment hardening, rate limiting, security headers, recovery, role administration or general account lifecycle management.

SEC-04 protects only `POST /api/game-servers` and the two Managed reads. The legacy administrative surface remains anonymous and powerful. It must not be publicly exposed in a multi-user deployment. SEC-05 must isolate container lifecycle, logs, backups, Palworld administration, scheduler, update, persistence, and system operations, and add an actor-aware audit trail.

GH-16B remains opt-in. Its real Docker validation must pass before Managed provisioning is activated outside isolated development, and the default test suite does not execute Docker.
