# GH-16B Controlled Docker Integration Validation

GH-16B validates the external Docker assumptions that the daemon-free GH-16 suite cannot prove: exact digest acquisition, RepoDigest and local image identity, a real host bind, creation by local image id, start, HEALTHCHECK, port publication, configuration preservation and restart persistence.

This validation is local, destructive to its own disposable container and potentially expensive. It must never run against a VPS, production Docker context, existing Palworld server, user save, production path or production GamesHud database.

## Status

The test infrastructure is prepared but has not been executed. Real Docker execution requires a separate human approval.

The test lives in `backend/tests/GamesHud.Api.Tests/DockerIntegrationValidationTests.cs`, carries `Category=DockerIntegration` and is skipped unless `GAMESHUD_RUN_DOCKER_INTEGRATION_TESTS=true`. A normal `dotnet test` with no opt-in cannot pull an image or create a container.

## Approved artifact

- Registry: `docker.io`
- Repository: `thijsvanloef/palworld-server-docker`
- Platform: `linux/amd64`
- Approved platform manifest digest: `sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9`
- Runtime reference: `docker.io/thijsvanloef/palworld-server-docker@sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9`

Tags such as `latest`, `v2`, `v2.7` and `v2.7.3` are not resolved by the test. Pull completion is not accepted as identity proof: a final inspect must expose the approved RepoDigest and a valid daemon local image id. Runtime creation then uses only that local image id.

## Prerequisites and resource guard

- Docker Desktop or Docker Engine reachable by the current user.
- Linux containers on an `amd64` daemon.
- At least 12 GiB assigned to the Docker daemon.
- At least 10 GiB currently available physical memory on the host.
- At least 30 GiB free on the temporary-filesystem drive.
- At least 30 GiB verified free in Docker's backing storage. Supply the observed value through `GAMESHUD_DOCKER_INTEGRATION_CONFIRMED_FREE_DISK_GB`; the test rejects values below 30.
- Registry and Steam network access with enough quota for several gigabytes.
- Permission to create, start, stop and remove one isolated container.
- `DOCKER_HOST` and `DOCKER_CONTEXT` unset. The harness uses only Docker.DotNet's local platform socket and rejects remote/shared context selection.

The 12 GiB daemon threshold leaves headroom above Palworld's 8 GiB minimum. The disk threshold covers the container image, Steam download, game installation and temporary test state. First boot may download or update Palworld, use substantial CPU/RAM, transfer several gigabytes and take 15–90 minutes. HEALTHCHECK receives the GH-16 600-second readiness window after start.

Do not install or reconfigure Docker automatically for this test. The harness rejects `DOCKER_HOST` and `DOCKER_CONTEXT`, then connects through the local platform socket. Stop if that local daemon is shared with production workloads.

## Isolation

Every run generates an opaque id and uses:

- namespace `gameshud-it-<id>`;
- server id `it-palworld-<id>`;
- container name `gameshud-it-palworld-<id>`;
- sandbox `<system-temp>/gameshud-it/<id>`;
- a SQLite database inside that sandbox;
- two dynamically selected UDP host ports;
- production container ports `8211/udp`, `27015/udp` and internal `8212/tcp`.

REST `8212/tcp` is exposed inside the container contract and receives no host binding. No network or volume is created. Docker's current managed `default` network policy is used and host networking is rejected.

Because the test process runs directly on the developer host, its API-visible and daemon-visible storage paths converge on the same exclusive sandbox path. The durable reservation still records both fields and the runtime mapping explicitly binds that host path to `/palworld:rw`. A future containerized API variant is needed only to validate two textually different real path namespaces.

## Validation layers

1. **Read-only preflight** — opt-in, daemon reachability/platform, daemon memory, available host memory, host and confirmed daemon disk, path containment, exclusive name and absence of an existing same-name container.
2. **Image acquisition** — inspect, exact digest/platform pull only when absent, final inspect, RepoDigest, local image id and durable `VerifiedLocalImageId`.
3. **Create and inspect** — real GamesHud storage/INI flow, creation while stopped by local image id, ownership labels, sandbox bind, published test ports, internal REST, restart policy, environment, resources, default networking and `Privileged=false`.
4. **Start and health** — real start, observed health transitions, Docker HEALTHCHECK healthy within 600 seconds and semantic INI preservation.
5. **Restart persistence** — stop and start only the captured test container, then prove the same container id, local image id, bind, ports and semantic configuration without another provisioning operation.

The INI is created before Docker start at `Pal/Saved/Config/LinuxServer/PalWorldSettings.ini` through the production managed-storage and Palworld configuration components. It contains an exclusive test server name, no password, no existing save and no production secret. `DISABLE_GENERATE_SETTINGS=true` is the only runtime environment entry.

## Approved execution command

Run this only after explicit human approval, from the `backend` directory. Confirm Docker Desktop's free backing-store capacity first and replace `30` with the observed whole-GiB value.

```powershell
$confirmation = Read-Host 'Type RUN GAMESHUD DOCKER INTEGRATION to pull Palworld and run one disposable server'
if ($confirmation -cne 'RUN GAMESHUD DOCKER INTEGRATION') { throw 'Docker integration validation cancelled.' }

$env:GAMESHUD_RUN_DOCKER_INTEGRATION_TESTS = 'true'
$env:GAMESHUD_DOCKER_INTEGRATION_CONFIRMED_FREE_DISK_GB = '30'
try {
    dotnet test tests/GamesHud.Api.Tests/GamesHud.Api.Tests.csproj --filter 'Category=DockerIntegration' --logger 'console;verbosity=detailed'
}
finally {
    Remove-Item Env:GAMESHUD_RUN_DOCKER_INTEGRATION_TESTS -ErrorAction SilentlyContinue
    Remove-Item Env:GAMESHUD_DOCKER_INTEGRATION_CONFIRMED_FREE_DISK_GB -ErrorAction SilentlyContinue
}
```

Do not run plain `dotnet test` while the opt-in variable is intentionally set. The filtered command makes the intended scope reviewable.

## Cleanup

The test uses `try/finally`. It captures the exclusive container id, proves its exact name and GamesHud ownership labels, stops it when needed and removes only that id with `RemoveVolumes=false`. It then deletes only the contained run sandbox.

The test never removes or tags an image and never invokes system, image or volume prune. The approved image is a shared daemon cache and remains installed. No generic name search is used for removal.

If cleanup fails, the test prints the exact captured id and sandbox path. Inspect before manual removal:

```powershell
docker inspect <exact-container-id>
docker rm -f <exact-container-id>
Remove-Item -LiteralPath '<exact-sandbox-path>' -Recurse -Force
```

Never substitute a name pattern, `docker system prune`, `docker image prune`, `docker volume prune` or broad filesystem path.

## Logs and troubleshooting

Detailed output includes the test namespace, approved digest, local image id, captured container id, allocated host ports, sandbox path, health transitions, observed durations and cleanup result. It contains no credentials or unrelated Docker resource inventory.

- **Skipped:** opt-in was absent. This is expected in the normal suite.
- **Daemon unavailable:** start Docker Desktop/Engine and verify the current local context.
- **Platform rejected:** switch Docker Desktop to Linux containers on `amd64`; do not bypass the guard.
- **Memory/disk rejected:** free resources or increase Docker Desktop allocation; do not lower the threshold ad hoc.
- **Digest/RepoDigest mismatch:** stop. Do not fall back to a tag or another digest.
- **Health timeout/unhealthy:** retain the printed diagnostic transitions; cleanup still targets only the captured test id.
- **Port race:** rerun after verifying no local process captured the two logged ephemeral UDP ports.

The test does not depend on Compose, Portainer, an external `gameshud-palworld` network, a running GamesHud deployment, an existing server or a VPS path.
