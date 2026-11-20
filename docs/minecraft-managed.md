# Managed Minecraft: Lunchtime

GH-MC-01B adds a deliberately narrow Managed installer for **Lunchtime: Cozy Create and Cooking v2.6.0-r2**. It is not a generic modpack uploader or arbitrary URL installer. The browser route is `/games/minecraft/install`; creation uses the authenticated, CSRF-protected `POST /api/game-servers` contract and durable ownership rules already used by Managed provisioning.

## Approved identities

The immutable artifact manifest is part of `minecraft.lunchtime.initial` schema 1 and is persisted before host or Docker mutation:

- CurseForge project `885927`, file `9716699`
- expected file `Lunchtime_Cozy_Create_and_Cooking_Server_Pack_v2.6.0-r2.zip`
- source `https://mediafilez.forgecdn.net/files/9716/699/Lunchtime_Cozy_Create_and_Cooking_Server_Pack_v2.6.0-r2.zip`
- size `755427795` bytes
- SHA-256 `4478a08dbcb57708f7a2fb29f4db51ef44e65806e9b8b618a6ec770dd0458d9e`
- Minecraft `1.20.1`, Forge `47.4.29`, Java `17`

The runtime image is `ghcr.io/itzg/minecraft-server:2026.11.0-java17`. Runtime authority is the approved `linux/amd64` platform manifest `ghcr.io/itzg/minecraft-server@sha256:633aab667752905d540e7723690ec0dcb19584492219ef074dd934b59e9d23a7`. The observed tag index digest `sha256:35f069ed801f4b14655acb52fdb7745dccfe69399867f57608ae4136a32c3c9e` is audit evidence, not runtime authority.

## Durable pipeline

Minecraft selects `gh-mc01-v1` without changing `gh09-v1` or `gh15-v2`:

1. `prepare_storage`
2. `acquire_image`
3. `acquire_game_artifact`
4. `configure_game`
5. `create_runtime`
6. `start_runtime`
7. `verify_health`
8. `complete`

The durable configuration records the display name, selected Java heap, separate container limit, requested host TCP port, explicit EULA acceptance, and the complete artifact identity. Idempotency hashes a canonical versioned request including these values; reusing a key for a different request is rejected by the existing Managed request store.

Artifact acquisition streams to an operation-specific sibling staging area, enforces the expected length and SHA-256, validates ZIP paths, duplicates, links, entry count, expanded size, per-entry size and compression ratio, and publishes with a directory rename. Recovery trusts only a marker bound to the immutable artifact identity plus required layout sentinels. Partial staging is retryable; unexpected content in the target is ambiguous/conflicting and is never adopted.

Only approved server data is copied: `config`, `datapacks`, `defaultconfigs`, `kubejs`, `modernfix`, `mods`, `schematics`, `server.properties`, and `server-icon.png`. Pack-provided start/install scripts, `old-mods-backup`, and the archive itself are not published or executed.

## Runtime contract and limitations

The approved itzg entrypoint starts exact Forge with `TYPE=FORGE`, `VERSION=1.20.1`, `FORGE_VERSION=47.4.29`, `MEMORY=<selected MiB>`, internal `SERVER_PORT=25565`, `ENABLE_RCON=FALSE`, and the explicitly authorized `EULA=TRUE`. The selected UI port is the host-side TCP binding; it does not change the pack's internal port. The container limit is the selected Java heap plus 1 GiB overhead.

Minecraft gets only its Managed `/data` mount, the declared TCP port, the GamesHud Managed network policy, bounded CPU/memory, and `unless-stopped`. It is not privileged, does not mount the Docker socket, and shares no Palworld/V Rising data volume. Docker `running` is not readiness: `verify_health` requires Docker health status `healthy`. The default Minecraft first-start window is 1200 seconds and can be overridden with `GAMESHUD_MINECRAFT_HEALTH_TIMEOUT_SECONDS` up to the existing 1800-second game cap.

The GH-MC-01A page remains exclusively for configured `LegacyExternal` Docker servers. Newly installed Minecraft servers remain `Managed` and use `/managed-servers/{id}` for durable provisioning status. Generic post-install Managed lifecycle/log UI is outside Dumb Mode Lite; no legacy container-ID endpoint is used to bridge that gap.

## VPS activation checklist

No item below was executed as part of GH-MC-01B.

- Confirm the VPS is `linux/amd64`, Docker is healthy, and the GamesHud API can reach GHCR and ForgeCDN over HTTPS.
- Confirm free host RAM for the selected heap **plus 1 GiB container overhead**, other services, the OS and Docker. Local development cannot establish VPS headroom.
- Confirm `/srv/gameshud` (or the configured `GAMESHUD_MANAGED_HOST_ROOT`) has more than 2 GiB free for download, staging, extracted server data and runtime growth.
- Confirm the selected TCP port (default `25565`) is free and allowed only by the intended firewall/security-group policy.
- Validate the tag still resolves to the approved `linux/amd64` digest. Stop on any mismatch; do not substitute another tag or digest.
- Set `GAMESHUD_MINECRAFT_HEALTH_TIMEOUT_SECONDS` only if the bounded default is operationally unsuitable.
- Deploy through the normal approved process, authenticate, review the fixed pack/name/RAM/port/EULA form, and schedule one installation.
- Observe all eight durable steps and retained safe failure state. Do not manually move staging directories or adopt an unexpected target.
- Confirm the container has no privilege, no Docker socket, only its own `/data`, the selected host port, and the expected memory limit.
- Confirm readiness reaches `healthy`, then connect with the matching client modpack. Separately verify graceful stop/restart and world persistence after explicit approval for operational testing.
- Keep GH-16B skipped until its own gate is approved.

## Operational validation still pending

Local tests use in-memory HTTP/ZIP fixtures and fake runtime inspectors. They prove policy, recovery and contracts without pulling an image, creating a container or running the pack. Real registry pull behavior, Forge bootstrap duration, image healthcheck behavior, RAM headroom, firewall reachability, client compatibility and graceful shutdown remain VPS homologation work requiring separate approval.
