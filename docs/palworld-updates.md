# Palworld Updates

GamesHud can check and manually apply Palworld dedicated server updates for the current temporary Palworld integration.

This is not automatic update scheduling and is not the future plugin system.

## Strategy

The supported strategy targets the current `thijsvanloef/palworld-server-docker` deployment model:

- The image installs or updates Palworld through SteamCMD when `UPDATE_ON_BOOT=true`.
- GamesHud checks Steam metadata from inside the configured Palworld container.
- GamesHud compares the installed Palworld server depot manifest from `/palworld/steamapps/appmanifest_2394010.acf` with the latest public depot manifest reported by Steam app info.
- GamesHud applies an update by running a safe manual maintenance flow and then starting the configured Palworld container so its own boot process runs SteamCMD.

GamesHud does not run `docker system prune`, remove volumes, delete saves, recreate containers, restart Docker, or manage Portainer/GamesHud containers.

## Requirements

The configured Palworld container must already have:

```text
UPDATE_ON_BOOT=true
```

GamesHud does not change the Palworld container environment. If `UPDATE_ON_BOOT` is not enabled, update apply is rejected before world save, backup, stop or start actions.

GamesHud verifies `UPDATE_ON_BOOT` from Docker's effective environment for the configured container, not from `appsettings`, `deploy/.env`, `compose.yaml` or `.env.example`. Changing deployment files does not change an already-created container. If the value is `false`, absent, invalid, or cannot be inspected, update apply fails closed with a safe `update_not_configured` response.

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
CONFIRM
```

## Status

Status returns:

- installed version from Palworld REST `/info` when available
- installed manifest for the Palworld server depot when available
- available version as the latest public Steam manifest when available
- update status
- last checked timestamp
- strategy name
- friendly message

GamesHud does not invent versions. If REST version or Steam manifest metadata cannot be read, those values are reported as unknown or unavailable.

## Check Update

GamesHud executes SteamCMD metadata checks inside the configured Palworld container. The preferred executable for the supported image is:

```text
/home/steam/steamcmd/steamcmd.sh
```

GamesHud falls back to `steamcmd.sh` or `steamcmd` from the container `PATH` only if the preferred path is not present. The command is bounded by `Palworld__Updates__CommandTimeoutSeconds`.

The Steam app info command is:

```text
+login anonymous +app_info_update 1 +app_info_print 2394010 +quit
```

It compares the latest public manifest for depot `2394012` against the local manifest recorded for depot `2394012` in `appmanifest_2394010.acf` when both are available. `GET /api/palworld/update` is read-only and does not save, back up, stop, start, restart, update, change configuration or send notifications.

The check response includes update readiness. It can report `update_available` while also reporting that update apply is not ready when the current container does not have `UPDATE_ON_BOOT=true`.

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
10. Wait for health check after the image startup/update sequence.
11. Re-read installed manifest information and require it to match the expected available manifest.
12. Return the result.

The actual SteamCMD update is performed by the Palworld container startup scripts when the container starts with `UPDATE_ON_BOOT=true`.

The post-start health check waits up to `Palworld__Updates__StartupVerificationTimeoutSeconds` and retries every `Palworld__Updates__VerificationRetryDelayMilliseconds`, because the production image can spend several minutes applying the update before the Palworld REST API is reachable again.

Only one update apply operation can run at a time in a single GamesHud API process. A duplicate request is rejected before save, backup, stop or start actions.

Failures are reported with safe error codes such as `update_not_configured`, `save_failed`, `backup_failed`, `stop_failed`, `update_failed`, `start_failed` and `verification_failed`. If the configured container was stopped and update preparation fails before startup, GamesHud attempts to start the configured container again. If startup fails after update preparation, GamesHud does not run the update preparation step again. If the container starts but the installed manifest remains old, GamesHud reports verification failure instead of update success.

## Rollback

GamesHud does not promise binary version rollback. SteamCMD downgrade or manifest pinning is not automated in this phase.

A world backup is mandatory before update. If the update flow fails after stopping the container, GamesHud attempts to start the configured Palworld container again when it is safe to do so.

## Sources

- `thijsvanloef/palworld-server-docker` documents `UPDATE_ON_BOOT`, automatic updates and the image's SteamCMD update-on-start behavior.
- Pocketpair's Palworld dedicated server guide documents SteamCMD app id `2394010`.
- Pocketpair's Palworld REST API documents `/announce` and `/save`.
