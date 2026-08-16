# Palworld REST API Integration

GamesHud uses the native Palworld REST API for server status, player information, metrics and world-save requests before backups. RCON is not used for this integration.

## Sources Checked

- Official Palworld REST API guide: `https://docs.palworldgame.com/api/rest-api/palwold-rest-api/`
- Official `info` endpoint: `https://docs.palworldgame.com/api/rest-api/info/`
- Official `players` endpoint: `https://docs.palworldgame.com/api/rest-api/players/`
- Official `settings` endpoint: `https://docs.palworldgame.com/api/rest-api/settings/`
- Official `metrics` endpoint: `https://docs.palworldgame.com/api/rest-api/metrics/`
- Official `save` endpoint: `https://docs.palworldgame.com/api/rest-api/save/`
- `thijsvanloef/palworld-server-docker` README: `https://github.com/thijsvanloef/palworld-server-docker`

The official REST API uses HTTP Basic Auth and should not be exposed directly to the Internet. Keep it reachable only from the backend or a private/internal network path.

## Palworld REST Endpoints Used

GamesHud currently uses:

```text
GET /v1/api/info
GET /v1/api/players
GET /v1/api/settings
GET /v1/api/metrics
POST /v1/api/save
```

Administrative REST operations such as announce, kick, ban, shutdown and force stop are intentionally not implemented.

## GamesHud Endpoints

GamesHud exposes internal contracts:

```text
GET /api/palworld/overview
GET /api/palworld/players
GET /api/palworld/metrics
POST /api/palworld/backups
```

The backend maps Palworld REST responses into GamesHud contracts. It does not return external API payloads directly.

## Configuration

Use environment variables:

```text
Palworld__ConnectionAddress=
Palworld__RestApi__BaseUrl=
Palworld__RestApi__Username=
Palworld__RestApi__Password=
Palworld__RestApi__TimeoutSeconds=5
```

`BaseUrl` may point to the REST root or directly to `/v1/api`.

Examples:

```text
Palworld__RestApi__BaseUrl=http://internal-palworld-rest:8212
Palworld__RestApi__BaseUrl=http://internal-palworld-rest:8212/v1/api
```

Do not hardcode VPS IPs, public hosts, usernames or passwords in source code or permanent documentation.

## Contracts

`GET /api/palworld/overview` returns:

- server name and description
- configured container name
- Docker container state/status
- GamesHud health classification
- Palworld version
- configured connection address
- players online and max players
- uptime, FPS, frame time, base count and in-game days when available
- sanitized player list

Health values:

- `healthy`
- `container-stopped`
- `container-starting`
- `rest-unavailable`
- `not-found`

`GET /api/palworld/players` returns:

- online count
- max players
- sanitized player list
- retrieval timestamp

Player entries include:

- name
- platform account name when available
- shortened public ID
- ping
- level

Player IP addresses, raw user IDs and credentials are not returned.

## Frontend Polling

The frontend polls overview and players every 15 seconds by default and pauses refresh while the browser tab is hidden. No WebSocket or SignalR is used in this phase.

## Security Notes

- Do not expose the Palworld REST API publicly.
- Do not send Palworld REST credentials to the frontend.
- Do not log the `Authorization` header or credentials.
- Do not use RCON for this integration.
- Keep additional write/admin REST actions for separate reviewed tasks.
