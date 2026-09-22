# Managed Palworld End-to-End Validation

GH-16 proves the internal Managed Palworld path from durable scheduling through worker finalization. The command commits a `pending_provisioning` server and `gh15-v2` operation, signals the executor and returns. A worker scope later discovers the operation and executes `prepare_storage`, `configure_game`, `acquire_image`, `create_runtime`, `start_runtime`, `verify_health` and `complete`. Finalization is verified through a fresh EF Core context as operation `succeeded`, server `running` and active slot released.

## Artifact and runtime contract

The approved artifact is `docker.io/thijsvanloef/palworld-server-docker@sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9` for `linux/amd64`. Acquisition inspects that reference, pulls it only after proven absence, performs a final inspect and persists the daemon local image id. Container creation uses only that local id.

The runtime specification has a deterministic GamesHud name and ownership labels, `DISABLE_GENERATE_SETTINGS=true`, minimum resource limits, `unless-stopped`, default network mode and `Privileged=false`. It preserves the image command and entrypoint. The managed host path binds to `/palworld:rw`; no client-defined image, environment, mount or command enters the specification.

Container ports remain fixed catalog values. Public `8211/udp` and `27015/udp` bind their planned host ports. Internal REST `8212/tcp` has no host reservation or host binding. A collision may change the host port while the Palworld container port stays `8211`; GamesHud does not compensate with Palworld port environment variables.

## Real components and fake boundaries

The integration harness uses the real plan builder, pipeline selection, scheduling service, EF Core SQLite stores, context loader, recovery policy, reconciliation application, provisioning engine, storage provider, Palworld configuration serializer/file store, image-acquisition step and adapter, runtime specification builder, mutation policy, Docker runtime adapter, create/start/health steps and atomic finalization. Storage directories and `PalWorldSettings.ini` use a real temporary filesystem with distinct API-visible and daemon-host roots.

Fakes exist only at external boundaries: host capability inspection, host port availability, host free-space inspection, secret storage, Docker image API, Docker container API and health delay. No test contacts a Docker daemon, registry, VPS or internet service.

The harness covers image already present and pull-required paths, late resolution of server/admin secret references, host-port collision, daemon `arm64` rejection before side effects, caller cancellation after commit and repeated discovery without duplicate create/start. It reopens SQLite to prove the image intent, storage paths, ports, game configuration, steps, operation and server lifecycle.

## Recovery and readiness

Restart scenarios interrupt after configuration, after a pull but before its checkpoint, after container create, after container start and during health polling. Trusted inspection either completes the effect or authorizes a bounded retry after proven absence. An unprovable create remains `provisioning_blocked`, retains the active slot and is never dispatched again. A known missing digest ends `provisioning_failed` and releases the slot. Completed steps awaiting finalization become `succeeded/running` in one transaction.

Readiness is the owned container running plus Docker HEALTHCHECK `healthy` when a health check exists. Managed Palworld uses a 600-second first-boot window, configurable up to 1800 seconds, without changing the generic 60-second default. UDP, query, REST and player readiness remain outside this contract.

## GH-16B controlled Docker validation

The next validation level must run in an isolated, disposable Docker environment and prove:

1. pull by the approved platform digest and capture of the daemon local image id and RepoDigest;
2. visibility and preservation of the generated INI through the daemon host bind;
3. container creation by local image id with the expected labels, environment, resources and `/palworld` bind;
4. published game/query mappings and absence of an internal REST host binding;
5. start and Docker HEALTHCHECK transition to healthy within the Palworld window;
6. persistence of configuration, image identity, ownership and recovery evidence across API and container restarts.

GH-16B must not use a production VPS or adopt an existing Palworld server. Cleanup must target only resources created by the isolated validation run.
