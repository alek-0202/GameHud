# Managed game server provisioning API

GH-17 exposes a small asynchronous API for managed provisioning. HTTP records the durable intent and returns; the ARCH-04 background worker owns execution.

## Create

`POST /api/game-servers` requires an opaque `Idempotency-Key` header.

```http
POST /api/game-servers
Idempotency-Key: 0199-example-create
Content-Type: application/json

{"gameId":"palworld","displayName":"My server"}
```

A valid request returns `202 Accepted`, a `Location` header for the server, and the generated `gameServerId` and `provisioningOperationId`. The response does not mean that host work has completed.

The backend normalizes the game ID and display name, then hashes a versioned canonical representation with SHA-256. It stores only the SHA-256 hash of the idempotency key. Reusing a key with the same semantic request returns the original IDs. Reusing it with a different request returns `409 Conflict`. A database unique index is the final concurrency authority.

Clients cannot select pipeline versions, images, digests, runtime configuration, ports, mounts, paths, environment variables, container identity, network settings, lifecycle, or operation state.

## Read state

- `GET /api/game-servers/{gameServerId}` returns the durable public server view.
- `GET /api/game-servers/{gameServerId}/provisioning` returns the durable operation, current step, ordered steps, attempt counts, timestamps, and a sanitized failure when present.

Suggested V1 clients poll provisioning about every two seconds while status is `pending` or `running`. On `succeeded`, navigate to the server view. On `failed`, show the safe error. `requiresInspection: true` means an operator must inspect the ambiguous or blocked operation; clients must not retry it blindly.

Server lifecycle values are `pending_provisioning`, `running`, `provisioning_failed`, and `provisioning_blocked`. Provisioning status values include `pending`, `running`, `succeeded`, and `failed`.

## Errors and security gates

Invalid requests return `400`; unknown games return `404`; known but unavailable provisioning returns `422`; idempotency and durable reservation conflicts return `409`. Public failures never include raw exceptions, secrets, host/API paths, Docker socket details, provider responses, or local image IDs.

Authentication and ownership enforcement remain a release gate. Rate limiting is deferred to API hardening. The project currently has no OpenAPI or API versioning foundation, so GH-17 does not introduce either framework.

Managed provisioning production or homologation activation remains gated on successful GH-16B validation against real Docker. GH-16B remains opt-in and is not run by the default test suite.
