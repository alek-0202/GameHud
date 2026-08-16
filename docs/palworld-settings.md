# Palworld Settings

GamesHud currently includes a personal Palworld settings editor before the planned plugin foundation. The editor is intentionally isolated from Docker Core and targets only the configured Palworld container.

## Sources

The local schema should be checked against current Palworld and container-image documentation before adding or changing keys:

- Official Palworld server configuration docs: `https://docs.palworldgame.com/settings-and-operation/configuration/`
- `thijsvanloef/palworld-server-docker` README: `https://github.com/thijsvanloef/palworld-server-docker`
- `thijsvanloef/palworld-server-docker` `.env.example`: `https://raw.githubusercontent.com/thijsvanloef/palworld-server-docker/main/.env.example`

Do not add a setting unless it can be mapped to a real `PalWorldSettings.ini` key. Container-only environment variables such as update, backup, Discord, Steam, user/group, Box64 or install-channel controls do not belong in this editor.

## Runtime Requirements

GamesHud edits:

```text
PalWorldSettings.ini
```

The Palworld deployment should keep generated settings disabled:

```text
DISABLE_GENERATE_SETTINGS=true
```

If Palworld keeps regenerating the file from environment variables, GamesHud changes can be overwritten.

## API Shape

`GET /api/palworld/config` returns:

- `containerName`
- `settings[]`

Each setting includes:

- real INI `key`
- friendly `label`
- `description`
- `category`
- typed `type`
- numeric `min`, `max` and `step` when known
- select `options`
- `defaultValue` when known
- `restartRequired`
- `advanced`
- `securitySensitive`
- current `value`
- `hasValue`

Password settings return `value: null` and use `hasValue` so plaintext passwords are never exposed.

`PUT /api/palworld/config` accepts:

```json
{
  "settings": [
    {
      "key": "ExpRate",
      "value": "2.0"
    }
  ]
}
```

Values are strings so the schema remains the source of truth for parsing and validation. Unknown keys are rejected, but unknown existing INI entries are preserved.

## Categories

The current schema is grouped for the UI:

- General
- World
- Player
- Pals
- Resources & Drops
- Building & Bases
- Guild
- Breeding
- Combat & PvP
- Advanced

The Basic view hides settings marked `advanced`. Advanced view shows all settings and allows search by label, description and INI key.

## Write Safety

The backend must preserve these guarantees:

- read only from the configured managed path
- preserve unknown existing INI keys
- reject unsupported update keys
- create a backup before writing
- write to a temporary file before replacing the settings file
- restore the backup when a write fails, when possible
- skip writes, backups and lifecycle actions when there are no effective changes
- when `restart=true`, stop and start only `Palworld__ContainerName`

## Passwords

`ServerPassword` and `AdminPassword` are security-sensitive password settings.

- GET must not expose plaintext values.
- Empty password updates preserve the existing value.
- Non-empty password updates write the new quoted INI value.

Do not log password values.

## Adding a Setting

To add a Palworld setting:

1. Confirm the real `PalWorldSettings.ini` key from the sources above.
2. Add a typed `PalworldSettingDefinition` in `PalworldSettingSchema`.
3. Include the label, description, category, type, default, limits or options where known.
4. Mark `advanced` for settings that are risky, uncommon, operationally sensitive or hard to explain quickly.
5. Mark `securitySensitive` for secrets, network exposure, authentication, public IP or mod-related settings.
6. Add or update backend tests for parsing, validation and serialization.
7. Let the frontend render it from the schema instead of hardcoding a new field.

Do not add settings by using `Dictionary<string, object>` or `dynamic`.
