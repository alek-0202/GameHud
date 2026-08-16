# Palworld Updates

GamesHud can check and manually apply Palworld dedicated server updates for the current temporary Palworld integration.

This is not automatic update scheduling and is not the future plugin system.

## Strategy

The supported strategy targets the current `thijsvanloef/palworld-server-docker` deployment model:

- The image installs or updates Palworld through SteamCMD when `UPDATE_ON_BOOT=true`.
- GamesHud checks Steam build metadata from inside the configured Palworld container.
- GamesHud applies an update by running a safe manual maintenance flow and then starting the configured Palworld container so its own boot process runs SteamCMD.

GamesHud does not run `docker system prune`, remove volumes, delete saves, recreate containers, restart Docker, or manage Portainer/GamesHud containers.

## Requirements

The configured Palworld container must already have:

```text
UPDATE_ON_BOOT=true
```

GamesHud does not change the Palworld container environment. If `UPDATE_ON_BOOT` is not enabled, update apply is rejected before world save, backup, stop or start actions.

Palworld REST API should be configured for:

- installed version from `/v1/api/info`
- players online from `/v1/api/metrics`
- update announcement through `/v1/api/announce`
- world save through `/v1/api/save`

## Endpoints

```text
GET /api/palworld/update
POST /api/palworld/update
```

`POST /api/palworld/update` requires exact confirmation:

```text
UPDATE PALWORLD SERVER
```

## Status

Status returns:

- installed version from Palworld REST `/info` when available
- available version as the latest public Steam build id when available
- update status
- last checked timestamp
- strategy name
- friendly message

GamesHud does not invent versions. If REST version or Steam build metadata cannot be read, those values are reported as unknown or unavailable.

## Check Update

GamesHud executes SteamCMD metadata checks inside the configured Palworld container:

```text
steamcmd +login anonymous +app_info_update 1 +app_info_print 2394010 +quit
```

It compares the latest public Steam build id against the local appmanifest build id when both are available.

## Apply Update

Manual update flow:

1. Verify an update is detected.
2. Verify `UPDATE_ON_BOOT=true`.
3. Read players online.
4. Announce update through REST when available.
5. Save the world through REST.
6. Create a `pre-update` backup.
7. Stop only the configured Palworld container.
8. Prepare the image-compatible update-on-boot step.
9. Start only the configured Palworld container.
10. Run health check.
11. Re-read version/build information.
12. Return the result.

The actual SteamCMD update is performed by the Palworld container startup scripts when the container starts with `UPDATE_ON_BOOT=true`.

## Rollback

GamesHud does not promise binary version rollback. SteamCMD downgrade or manifest pinning is not automated in this phase.

A world backup is mandatory before update. If the update flow fails after stopping the container, GamesHud attempts to start the configured Palworld container again when it is safe to do so.

## Sources

- `thijsvanloef/palworld-server-docker` documents `UPDATE_ON_BOOT`, automatic updates and the image's SteamCMD update-on-start behavior.
- Pocketpair's Palworld dedicated server guide documents SteamCMD app id `2394010`.
- Pocketpair's Palworld REST API documents `/announce` and `/save`.
