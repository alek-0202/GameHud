# Metrics

GamesHud collects lightweight host, Docker and Palworld metrics without Prometheus, Grafana, Redis or a database.

## Endpoints

```text
GET /api/system/metrics
GET /api/system/metrics?historyHours=1
GET /api/containers/{containerId}/metrics
GET /api/palworld/metrics
GET /api/palworld/metrics?historyHours=1
GET /api/palworld/metrics?historyHours=6
GET /api/palworld/metrics?historyHours=24
```

Responses are GamesHud contracts. Docker SDK objects and raw Docker stats payloads are not returned.

## Sources

Host metrics use Linux host files and filesystem APIs:

- `/proc/stat` for CPU deltas
- `/proc/meminfo` for memory
- `/proc/uptime` for uptime
- filesystem information for disk usage

Container metrics use Docker Engine API stats with one-shot snapshots. GamesHud does not keep a permanent stats stream open.

Palworld metrics combine:

- Docker stats for the configured Palworld container
- Palworld REST `/v1/api/metrics` for players, max players and server uptime when available

## History

History is stored in memory through `InMemoryMetricsHistoryStore`.

Default collection interval:

```text
60 seconds
```

Default retention:

```text
24 hours
```

SQLite is not used in this phase because the requested history is short, operational persistence across API restarts is not currently required, and in-memory storage avoids adding a database volume before there is durable product state.

## Configuration

```text
Metrics__SnapshotIntervalSeconds=60
Metrics__RetentionHours=24
Metrics__HostDiskPath=/
```

Retention is bounded by the API to avoid unbounded history queries.

## Frontend

The dashboard polls metrics every 30 seconds. Palworld Overview supports simple history windows:

- 1h
- 6h
- 24h

The charts are small inline sparklines. No frontend chart dependency is used in this phase.
