# Secrets Management

SEC-02 establishes the GamesHud secret-management foundation used before generic provisioning.

This foundation separates secret material from normal application data. It does not add users, authentication, authorization, Vault/KMS integration, Agent credentials, public secret CRUD, provisioning or Palworld secret migration.

## Model

GamesHud represents stored secrets with provider-neutral types:

- `SecretId`: opaque GUID identifier.
- `SecretReference`: application reference containing only the opaque id.
- `SecretPurpose`: classification such as `game_server_password`, `game_admin_password`, `game_rest_credential`, `webhook` or `integration_token`.
- `SecretValue`: backend-only plaintext wrapper whose `ToString()` and JSON serialization do not reveal plaintext.

Purpose is classification, not authorization. Future authorization must not rely on purpose alone.

Application records should persist `SecretReference`, not filesystem paths, encryption identifiers, plaintext hashes, provider details or storage-specific ids.

## Store Boundary

`ISecretStore` is the use-case boundary:

- store a new secret and return a `SecretReference`;
- retrieve material only inside authorized backend code;
- replace material while preserving the reference;
- delete stored material when the provider can.

Callers do not know the file location, encryption algorithm, key source or provider implementation. The provider can be replaced by a future OS, Vault, KMS or database-backed implementation without changing application contracts.

## Local Provider

The initial local provider stores encrypted files under:

```text
<DataRoot>/system/secrets
```

`DataRoot` is the same backend-derived root used by storage and persistence. HTTP clients cannot provide secret-store paths.

Secret files contain safe metadata plus encrypted material:

- secret id;
- purpose;
- created and updated UTC timestamps;
- version and algorithm metadata;
- nonce, ciphertext and authentication tag.

Secret material is not stored in SQLite and is not stored as plaintext in temp files. Writes use a ciphertext temp file followed by an atomic replace/move. In-process per-secret locks prevent concurrent writes from corrupting the same secret. This is not a distributed lock.

## Cryptography And Key Management

The local provider uses .NET `AesGcm` with a 256-bit key and authenticated encryption. GamesHud does not implement custom crypto, XOR, base64-as-encryption or hardcoded keys.

The initial key strategy is an external bootstrap secret:

```text
Secrets__MasterKey=<base64 32-byte key>
```

The key must be generated and stored outside the repository. It must not be committed, hardcoded, placed in `appsettings.json`, copied into examples or stored next to ciphertext as protection.

This bootstrap choice is intentionally simple and replaceable. It means:

- anyone who can read the environment/config source containing `Secrets__MasterKey` can decrypt the local store;
- losing the key makes stored secrets unrecoverable;
- rotating the key requires future migration/re-encryption work;
- the provider cannot protect against an attacker with process, root or administrator control.

If the key is missing or invalid, the store is unavailable and secret operations fail closed. GamesHud must not silently fall back to plaintext, in-memory or unencrypted storage.

## Threats And Limits

This foundation reduces accidental exposure from database theft, source control, normal API responses, normal logs and filesystem reads without the bootstrap key.

It does not protect against:

- an attacker controlling the GamesHud process;
- root/administrator access to the machine;
- memory inspection of managed .NET strings;
- malicious code running in the API process;
- insecure deployment of the bootstrap key;
- compromised backups that include both ciphertext and bootstrap key.

`SecretValue` narrows accidental string spreading, but .NET strings are immutable managed memory and cannot provide secure zeroization guarantees.

## API And Logging Rules

There is no public generic secret CRUD API.

`GET /api/system/secrets` returns only minimal readiness state. It must not return secret ids, purposes, counts, filesystem paths, provider paths, key details or secret material.

Normal APIs must never return stored secret material. If a UI needs to show whether something is configured, return a boolean or safe metadata such as `configured: true`.

Logs, exceptions and problem details must not include plaintext secret values, webhook URLs, REST credentials, bearer tokens, private keys or request bodies from secret-bearing endpoints.

## Legacy Palworld Configuration

Current Palworld sources remain `LegacyExternal`:

- `ServerPassword` and `AdminPassword` in `PalWorldSettings.ini`;
- Palworld REST credentials from backend configuration;
- Discord webhook URL from backend configuration.

SEC-02 does not migrate, import, adopt or rewrite those values into the secret store. Existing integrations may continue reading legacy configuration until a later provisioning or migration task explicitly changes the flow.

## GH-08 Contract

Future provisioning may accept secret material during a create-server workflow, store it through `ISecretStore`, and persist only `SecretReference` in durable server records.

Database transactions, secret-store writes and Docker/filesystem provisioning are separate consistency domains. GH-08 must record explicit step state and handle partial failure without pretending one transaction covers everything.

## Backup And Deletion

Game backups are not secret-store backups. Secret files must not be included in game backup archives by default.

Delete removes the provider's stored material, but GamesHud does not promise secure erase on SSDs, journaling filesystems, snapshots or external backups.
