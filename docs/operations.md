# Operations

GamesHud includes temporary operational tools for logs, safe schedules and optional Discord notifications.

These tools are local to the current API process. Scheduler state is kept in memory and is reset when the API restarts.

## Logs

Container logs remain bounded snapshots, not streams.

Endpoint:

```text
GET /api/containers/{containerId}/logs?tail=200&timestamps=true&stream=all&search=error
```

Query parameters:

- `tail`: recent line count, from `1` to `2000`.
- `timestamps`: asks Docker to include timestamps.
- `stream`: `all`, `stdout` or `stderr`.
- `search`: optional case-insensitive backend filter, up to `120` characters.

The response keeps the legacy `lines` array and also returns typed `entries` with:

- `message`
- `stream`
- `severity`
- `timestamp`

Log content is plain text. The frontend must not interpret it as HTML.

## Scheduler

Endpoint:

```text
GET /api/scheduler
POST /api/scheduler
POST /api/scheduler/{id}/run
```

Supported action types only:

- `automatic-backup`
- `restart-palworld`
- `update-check`
- `announcement`
- `shutdown-palworld`

The scheduler never accepts arbitrary shell commands.

Each task has:

- `id`
- `actionType`
- `recurrenceMinutes`
- `enabled`
- `nextRunAt`
- `lastRunAt`
- `lastResult`
- `status`
- optional `message`

Disabled tasks are not executed. Duplicate execution of the same task is rejected while a task is already running.

Scheduled Palworld restart announces maintenance through the Palworld REST API, requests a world save, waits the configured safety delay and restarts only the configured Palworld container.

## Discord Notifications

Discord webhook support is optional and backend-only.

Endpoint:

```text
GET /api/settings/notifications
POST /api/settings/notifications/test
```

The API never returns the webhook URL to the frontend. The frontend only receives whether a webhook is configured and which event groups are enabled.

Configuration:

- `Notifications__Discord__WebhookUrl`
- `Notifications__Discord__CooldownSeconds`
- `Notifications__Discord__Events__ServerStatus`
- `Notifications__Discord__Events__Backups`
- `Notifications__Discord__Events__Updates`
- `Notifications__Discord__Events__PlayerJoinLeave`

Event groups:

- server status
- backups
- updates
- player join/leave

Notifications use cooldown and deduplication keys to avoid repeated spam.
