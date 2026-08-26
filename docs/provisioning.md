# Provisioning Engine Foundation

GH-08 introduces the internal provisioning coordination boundary. It does not expose a create-server API and does not create a container, directory, volume, network, firewall rule, game installation or configuration file.

## Flow

```text
CreateGameServerProvisioningRequest
-> Validate host and game requirements
-> Build ValidatedProvisioningPlan
-> Plan ports and storage
-> Reserve server, ports, storage and Pending operation atomically
-> Run the safe step pipeline
-> Persist Succeeded or Failed
```

Planning, reservation and runtime state remain distinct:

```text
plan != reservation
reservation != host mutation
operation succeeded != runtime ready
```

The managed server remains `pending_provisioning` after the GH-08 foundation succeeds because no runtime exists yet. GH-12 and GH-13 will introduce real game provisioning and health semantics.

## Contracts

`CreateGameServerProvisioningRequest` accepts only `GameServerId`, `GameId` and `DisplayName`. Clients cannot supply host paths, images, mounts, Docker flags, privileged mode or shell commands.

`ValidatedProvisioningPlan` contains only backend-approved decisions: normalized identities, selected runtime from the game definition, host compatibility status and warnings, selected ports, managed relative storage paths, opaque `SecretReference` values and required step ids. It contains no `SecretValue` and no client-supplied host mutation data.

`ProvisioningContext` is typed and carries the operation id, game definition, validated plan, durable reservation result and typed step executions. It does not use `dynamic` or an untyped state dictionary.

## Pipeline

The modeled steps are `validate_host`, `plan_resources`, `reserve_resources`, `prepare_storage`, `configure_game`, `create_runtime`, `start_runtime`, `verify_health` and `complete`.

Validation and planning occur before durable state. Reservation creates the `Pending` operation transactionally. The engine then persists `Running` and `CurrentStep` before each executable step. In GH-08, all host-facing steps use `NoHostMutationProvisioningStep`; they are explicitly skipped and perform no I/O. The final coordination step succeeds.

The engine is game-neutral. Game-specific requirements, ports, storage and runtime choices come from `GameDefinition` and existing planners. There is no Palworld branch in the engine.

## Failure And Recovery

A failed result or unexpected exception stops the pipeline, preserves the failing `CurrentStep`, stores only a safe error code/message, clears the active slot and marks the operation `Failed`. Unexpected exception text is never persisted as the safe message.

Cancellation temporarily maps to `Failed` with `provisioning_cancelled`; GH-09 may add a dedicated cancelled state. Completed compensating steps are invoked in reverse order. Durable compensation attempts, retries and complete rollback remain GH-15 work.

Only `Pending -> Running|Failed` and `Running -> Running|Succeeded|Failed` progression is allowed. Terminal operations cannot return to `Running`.

`IManagedServerStore.GetIncompleteOperationsAsync` returns durable `Pending` and `Running` operations after a process restart. Startup does not execute, fail or recover them automatically; GH-09 will define recovery policy and the full state machine.

## Idempotency And Concurrency

An active operation returns `operation_in_progress`; an already managed server returns `duplicate_server`. Durable unique constraints remain authoritative for same-server, port and storage conflicts. Conflicts do not trigger an unpredictable replan loop.

Different server ids do not share a global application lock. Their reservations may coexist when persistent port and storage constraints do not conflict.

No public provisioning endpoint or frontend workflow is added in GH-08. A future API must distinguish preview, reservation and actual runtime creation rather than claim a server was created by this foundation.
