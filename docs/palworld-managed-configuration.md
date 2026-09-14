# Palworld Managed Configuration

GH-14 implements `configure_game` for Managed Palworld servers without changing pipeline `gh09-v1` or invoking
Docker. The step materializes the initial `PalWorldSettings.ini` at the fixed path
`Pal/Saved/Config/LinuxServer/PalWorldSettings.ini` below the owned `data` storage reservation, whose trusted
runtime mount is `/palworld`.

## Intent And Secrets

The `palworld.initial` schema version 1 record loaded through `IGameProvisioningConfigurationStore` is the only
source of truth. The original request, in-memory provisioning plan, existing file, legacy `PalworldOptions`, Docker
state, and environment are not configuration sources. The file is only a derived, reconciliable effect.

The durable record contains role-specific `ServerPasswordSecretReference` and
`AdminPasswordSecretReference` values. `ISecretStore` resolves each optional reference immediately before
serialization; absent references produce empty Palworld password values. Plaintext is not added to SQLite,
provisioning state, logs, errors, runtime specifications, Docker environment, or labels. Palworld requires
plaintext in the final INI, which remains an accepted residual risk. The current `ISecretStore.GetAsync` contract
does not return `SecretPurpose`, so GH-14 preserves the two typed roles but cannot independently revalidate stored
purpose without redesigning the secret boundary.

The durable difficulty values remain `None`, `Normal`, and `Hard`. Only the Palworld boundary maps `Hard` to the
format value `Difficult`; parsing maps it back for semantic comparison. No schema change or migration is needed.

## Serialization And Drift

The Managed serializer emits a closed, deterministic two-line configuration containing only `ServerName`,
`ServerDescription`, `ServerPlayerMaxNum`, `Difficulty`, `ServerPassword`, and `AdminPassword`. Quotes and
backslashes are escaped. CR, LF, tabs, NUL, and all other control characters are rejected before a write. Commas,
parentheses, empty optional strings, and Unicode remain inside quoted values.

The matching minimal parser accepts only that closed property set and safe equivalent ordering/spacing. An
existing semantically equal file succeeds without rewrite or timestamp change. Invalid or divergent content is
never overwritten or adopted; execution fails closed and reconciliation reports `ambiguous`.

## Filesystem Boundary

The target is reconstructed from the Managed server, operation, reservation ownership, and backend-controlled
layout. LegacyExternal and cross-server state fail before filesystem access. The writer checks lexical containment,
requires the prepared Managed roots, validates existing child ancestry and final files for symlink/reparse points,
and creates only the fixed Palworld child directories.

Writes use a `CreateNew` temporary file in the destination directory named
`.PalWorldSettings.ini.gameshud-<operation-id>-<opaque-guid>.tmp`. The complete UTF-8 content is written, flushed,
flushed to disk when supported by the concrete stream, closed, and revalidated before a no-overwrite rename in the
same directory. Cleanup is best effort and is limited to the writer's controlled temp name. The final file is never
deleted by compensation and is never blindly replaced.

These checks reduce link and traversal attacks, but pathname validation and mutation are separate operating-system
calls. A privileged concurrent actor can still race a path component between checks (residual TOCTOU); GamesHud
does not claim descriptor-relative or filesystem-transaction guarantees that .NET's portable APIs do not provide.

## Reconciliation And Crash Windows

Reconciliation performs no mutation:

- `effect_exists`: the final file is a safe regular file, durable intent and required secrets resolve, parsing
  succeeds, and values are semantically equal.
- `effect_absent`: the final file is absent, no controlled temp artifact exists, and the filesystem state is safe.
- `ambiguous`: drift, invalid content, unsafe paths, relevant temp artifacts, missing secrets, access errors, or any
  state that cannot be proven.

This classifies a crash before writing as absent, a crash with only a temp as ambiguous, and a crash after rename but
before checkpoint as exists. A temp-write error is absent only when cleanup and subsequent inspection prove no
relevant artifact; otherwise it is ambiguous. Later final-file drift is ambiguous.

The trusted Palworld definition must continue supplying `DISABLE_GENERATE_SETTINGS=true`; otherwise the container
could regenerate the materialized file. GH-14 performs no create/start/stop/restart, pull, inspect, exec, REST, RCON,
or other Docker/runtime action.
