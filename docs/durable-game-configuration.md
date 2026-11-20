# Durable Game Provisioning Configuration

GH-14A persists validated configuration intent, never resolved secret values:

```text
user intent -> typed game validation -> opaque SecretReferences -> durable record
-> restart/recovery -> trusted codec -> typed configuration -> future configure_game
```

`ManagedGameConfigurationRecord` belongs to a Managed GameServer. The server, initial configuration, provisioning
operation, steps, and resource reservations are inserted in the same database transaction. A unique
`GameServerId + ConfigurationKind` index prevents multiple active initial intents. `Version` is an optimistic
concurrency token and `SchemaVersion` must be positive.

Palworld uses backend-controlled kind `palworld.initial`, schema version `1`, and a closed JSON payload produced
only by `PalworldProvisioningConfigurationCodec`. Its typed fields are server name, description, maximum players,
difficulty, and optional semantic server/admin password references. Payload deserialization rejects unknown fields,
wrong game/kind, unsupported versions, malformed JSON, and invalid values. It never resolves a secret.

The durable intent is the source of truth; a future `PalWorldSettings.ini` is only a derived reconciliable effect.
Payloads and secret identifiers must not be logged. LegacyExternal servers do not receive or adopt this state.
