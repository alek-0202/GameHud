# Managed game server provisioning API

GH-17 exposes a small asynchronous API for managed provisioning. HTTP records the durable intent and returns; the ARCH-04 background worker owns execution. SEC-04 protects this surface with an ASP.NET Core Identity cookie and owner-scoped queries.

## Session and antiforgery

The SPA uses the same origin as `/api`. It first calls `GET /api/auth/csrf`, then sends the returned `requestToken` in the `X-CSRF-TOKEN` header on login, logout, and managed create requests. The separate authentication cookie is HttpOnly and is never read by JavaScript.

- `POST /api/auth/login` creates the cookie and returns only the public user id and email.
- `POST /api/auth/logout` requires authentication and antiforgery validation.
- `GET /api/auth/me` returns the current minimal session projection.
- Public registration is not mapped. Closed-alpha accounts can only be created through the internal account-provisioning application boundary; no operational CLI or public admin endpoint exists yet.

Anonymous Managed API requests return `401` without an HTML redirect. Development clients should use a Vite `/api` proxy to preserve the same-origin model.

## Create

`POST /api/game-servers` requires an opaque `Idempotency-Key` header.

```http
POST /api/game-servers
Idempotency-Key: 0199-example-create
X-CSRF-TOKEN: request-token-from-api-auth-csrf
Content-Type: application/json

{"gameId":"palworld","displayName":"My server"}
```

A valid request returns `202 Accepted`, a `Location` header for the server, and the generated `gameServerId` and `provisioningOperationId`. The response does not mean that host work has completed.

The backend derives `OwnerId` only from the authenticated principal, normalizes the game ID and display name, then hashes a versioned canonical representation with SHA-256. It stores only the SHA-256 hash of the idempotency key. Idempotency belongs to the owner: reusing a key for the same owner and semantic request returns the original IDs; changing the request returns `409 Conflict`; another owner may use the same key independently. Database unique indexes are the final concurrency authority.

Clients cannot select pipeline versions, images, digests, runtime configuration, ports, mounts, paths, environment variables, container identity, network settings, lifecycle, or operation state.

## Read state

- `GET /api/game-servers/{gameServerId}` returns the durable public server view.
- `GET /api/game-servers/{gameServerId}/provisioning` returns the durable operation, current step, ordered steps, attempt counts, timestamps, and a sanitized failure when present.

Both reads filter by `OwnerId` in the query. Another owner's id and a historical resource with `OwnerId = NULL` both return `404`. The worker continues from durable ids after the HTTP request and does not depend on `HttpContext`.

Suggested V1 clients poll provisioning about every two seconds while status is `pending` or `running`. On `succeeded`, navigate to the server view. On `failed`, show the safe error. `requiresInspection: true` means an operator must inspect the ambiguous or blocked operation; clients must not retry it blindly.

Server lifecycle values are `pending_provisioning`, `running`, `provisioning_failed`, and `provisioning_blocked`. Provisioning status values include `pending`, `running`, `succeeded`, and `failed`.

## Errors and security gates

Invalid requests return `400`; unknown games return `404`; known but unavailable provisioning returns `422`; idempotency and durable reservation conflicts return `409`. Public failures never include raw exceptions, secrets, host/API paths, Docker socket details, provider responses, or local image IDs.

Rate limiting is deferred to SEC-06. The project currently has no OpenAPI or API versioning foundation, so GH-17 does not introduce either framework.

SEC-04 covers only the three Managed routes above. The legacy administrative API can control containers, logs, backups, Palworld administration, schedules, updates, and system operations and must not be publicly exposed in a multi-user deployment. SEC-05 must isolate that surface before public exposure.

Managed provisioning production or homologation activation remains gated on successful GH-16B validation against real Docker. GH-16B remains opt-in and is not run by the default test suite.
