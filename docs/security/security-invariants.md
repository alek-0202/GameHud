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
- GamesHud must never automatically delete, overwrite, adopt or migrate External resources.
- Managed resource deletion must require durable ownership proof and auditability.
- Backups must be stored outside the managed game data path.
- Restore, delete, update and destructive player actions must require strong server-side confirmation or stronger future authorization controls.
- Scheduler actions must remain allowlisted; arbitrary shell, Docker exec, compose and user-defined commands are not valid schedule actions.
- Future command execution must not interpolate untrusted input into shell commands.
- Future Agent commands must require mutual authentication, command authorization and replay protection.
- Secrets persistence must use a dedicated secret model before secrets are stored in durable application state.
- Public or multi-user operation must have audit logging for destructive and privileged actions.

