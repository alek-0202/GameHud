# Security Invariants

These invariants are permanent GamesHud security rules. Future implementation tasks may refine controls, but must not weaken these rules without an explicit architecture decision.

## Invariants

- GamesHud must not be exposed publicly until authentication, authorization and public deployment hardening exist.
- The frontend must never access Docker Engine, the Docker socket, host filesystem mounts or backend-only secrets directly.
- The Docker socket must never be exposed over TCP or sent to the frontend.
- Client input must never become an arbitrary host filesystem path.
- Game definitions and plugins must not contain host-specific secrets or personal paths.
- Normal API responses must never return plaintext secrets, tokens, webhook URLs, private keys or Palworld REST credentials.
- Docker SDK and external service models must be mapped into GamesHud contracts before reaching clients.
- Mutating operations must be explicit and must not be hidden behind read-only planning, inspection or catalog endpoints.
- Planning endpoints must not create directories, reserve durable state, open firewall rules, publish ports, create containers, create volumes or mutate host state.
- Provisioning must only mutate resources included in a validated plan.
- Provisioning failure must never silently claim success.
- A provisioning mutation in an unknown execution state must never be blindly retried.
- External side effects must be reconciled before retry when completion cannot be proven from durable state.
- Terminal provisioning state must never transition back to running implicitly.
- GamesHud must never automatically delete, overwrite, adopt or migrate External resources.
- Managed resource deletion must require durable ownership proof and auditability.
- Backups must be stored outside the managed game data path.
- Restore, delete, update and destructive player actions must require strong server-side confirmation or stronger future authorization controls.
- Scheduler actions must remain allowlisted; arbitrary shell, Docker exec, compose and user-defined commands are not valid schedule actions.
- Future command execution must not interpolate untrusted input into shell commands.
- Future Agent commands must require mutual authentication, command authorization and replay protection.
- Secrets persistence must use a dedicated secret model before secrets are stored in durable application state.
- Secret material must never be persisted as normal application data or plaintext database fields.
- Durable resource records must reference stored secrets by opaque secret references, not provider paths or plaintext-derived identifiers.
- Secret storage failure must never downgrade to plaintext, in-memory or unencrypted storage.
- Secrets must never be intentionally logged.
- Encryption keys and bootstrap secrets must never be hardcoded, committed or stored next to ciphertext as protection.
- Public or multi-user operation must have audit logging for destructive and privileged actions.
- Runtime mutation is deny-by-default and client input must never become provider options directly.
- Only a validated runtime specification may cross the runtime adapter boundary.
- Runtime images must be backend-controlled game-definition metadata.
- Only managed reservations owned by the same game server and operation may become mounts or port bindings.
- Game containers must never receive the Docker socket, privileged mode, host namespaces, arbitrary devices, capabilities, security options, commands, entrypoints or client-provided environment dictionaries.
- Plaintext secrets must never enter persisted or logged runtime specifications; use opaque `SecretReference` values.
- Runtime adapters must not expose arbitrary command, argument or generic options execution surfaces.
- Runtime provider invocation must pass through the mutation execution boundary.
- Provider exceptions must not cross the runtime boundary as raw public or persisted errors.
- Cancellation after provider invocation does not prove that a mutation is absent.
- Compensation requires proven Managed ownership and a known applied effect.
- Provider resource identities must be backend-controlled and stable for reconciliation.
