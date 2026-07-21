# API Guidelines

## Scope

These rules apply to the GamesHud .NET backend.

---

## Controllers

- Controllers should be thin.
- Controllers must not access external SDKs directly.
- Controllers should validate HTTP input, delegate work to services and produce HTTP responses.
- Do not place extensive infrastructure rules in controllers.

---

## Contracts

- The API must expose GamesHud contracts.
- Docker.DotNet types and other SDK models must not be returned directly.
- DTOs should be predictable, small and documentable.
- Sensitive data must not appear in public contracts.

---

## Services

- Docker communication must remain isolated in dedicated services.
- Do not create Repository, Unit of Work, CQRS, Mediator or additional layers without a concrete need.
- Interfaces should exist when they bring real value, especially SDK isolation and testability.

---

## Container Lifecycle

- Lifecycle actions are limited to explicit manual start, stop and restart operations.
- Lifecycle actions must be exposed as `POST` endpoints.
- Start should return a friendly success response when the container is already running.
- Stop should return a friendly success response when the container is already stopped.
- Stop and restart timeout values must be validated before reaching Docker services.
- The lifecycle timeout default is `10` seconds, with a minimum of `1` and a maximum of `120`.
- Do not add kill, pause, unpause, remove, rename, exec, recreate, pull, update, compose, scheduled or batch lifecycle operations without a new roadmap decision.

---

## Configuration

- Use `appsettings`, environment variables and secret storage.
- Never hardcode IPs, endpoints, passwords, tokens, personal paths or VPS addresses.
- Environment variables should follow ASP.NET Core configuration conventions.

---

## Error Handling

- Do not return stack traces.
- Expected failures should have consistent HTTP responses.
- Docker-unavailable cases should be handled with a friendly response.
- Unexpected errors should be logged with `ILogger`.
- Do not log secrets or sensitive payloads.
- Do not silently swallow exceptions.

---

## Cancellation

- Async operations should accept and propagate `CancellationToken` when applicable.
- Streams and disposable resources should be released correctly.

---

## Security

- The Docker socket is equivalent to high privilege on the host.
- Only the backend may access Docker Engine.
- Never expose complete container environment variables.
- Avoid exposing raw inspect payloads, secrets, tokens and unnecessary internal information.

---

## Testing

- Tests must not depend on a real VPS or production Docker host.
- Use mocks, fakes or test doubles.
- Cover contracts, mapping, errors and relevant regressions.
- Do not create fragile tests based only on text or file names when behavioral tests are possible.

---

## Validation

Run backend validation when backend behavior or runtime configuration changes:

```bash
dotnet restore
dotnet build
dotnet test
```
