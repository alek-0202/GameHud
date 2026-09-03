# Runtime Mutation Execution

GH-10 adds the provider-independent execution boundary after SEC-03 validation. It performs no Docker or filesystem mutation.

```text
ValidatedRuntimeMutationSpecification
-> RuntimeMutationExecutionContext
-> IRuntimeMutationExecutor
-> typed IGameRuntimeAdapter
-> future provider
```

Only `CreateRuntime` is modeled. The context carries the validated specification, persisted step identity, attempt, deterministic mutation execution identity and backend-controlled provider resource identity. It contains no HTTP context, plaintext secret, arbitrary environment, command or generic provider options.

The execution identity is stable as `<OperationId>:<StepId>:<MutationKind>` and does not change across attempts. Provider resource identity is derived from `GameServerId`; clients cannot supply names or labels. Future providers may persist a provider identifier when a real resource exists, but must retain this ownership relationship.

Outcomes are:

- `success`: completion is known.
- `known_failure`: the provider reports a safe deterministic failure and reconciliation is not required.
- `unknown`: application of the side effect cannot be proven; retry requires inspection/reconciliation.
- `cancelled_before_invocation`: the provider was not called and absence is known.

Provider exceptions and cancellation after provider invocation are translated to safe `unknown` outcomes. Raw exception messages never cross the boundary. No generic retry loop exists.

GH-09 persists `Running` before invoking the step. Therefore the crash windows are conservative: a crash before provider invocation leaves a running mutation for inspection; a crash during/after a provider effect but before the success checkpoint is reconciled; a returned success followed by a crash before checkpoint is also reconciled. None is blindly retried.

The existing `IProvisioningStepReconciler` outcomes remain `effect_exists`, `effect_absent` and `ambiguous`. Compensation is allowed only after a known applied effect, supported compensation and proven `Managed` ownership. LegacyExternal resources are never mutation or compensation targets.

GH-12 supplies the first real provider call: Docker container creation only. Docker SDK cancellation is propagated before dispatch; any exception, cancellation, timeout, or lost response from `CreateContainerAsync` is classified as unknown and requires reconciliation. No adapter retry is performed.

The Docker adapter uses Docker.DotNet and maps the validated specification internally to `CreateContainerParameters`. It first proves that the trusted image exists locally and that no conflicting deterministic identity exists. It never pulls an image and never starts, stops, restarts, removes, executes in, creates a network for, or creates a volume for a container. Successful create captures Docker's returned ID only as immediate proof; recovery uses the stable `gameshud-<GameServerId>` name and ownership labels, so no schema migration is required.

Real reconciliation lists all container states and then inspects the unique candidate. `effect_exists` requires the deterministic name, complete GamesHud ownership labels, trusted image, stopped state, bind set, public port bindings, resource limits, restart policy, non-privileged mode, and default network mode to match. Missing identity is `effect_absent`; collision, multiple candidates, provider failure, running state, or critical drift is `ambiguous`.

GH-13 evolves the closed mutation kind with `StartRuntime`. The same executor invokes a dedicated typed adapter method; it does not accept an operation string or Docker options. Start reconciliation treats a proven running runtime as `effect_exists`, proven created/exited as `effect_absent`, and paused/restarting/dead/foreign/drift/provider failure as ambiguous. Readiness inspection does not pass through the mutation executor.

GH-11 applies the same execution principles to managed storage. `prepare_storage` validates and reloads durable state before calling its typed filesystem provider. Local deterministic failures are known failures; inaccessible or unsafe reconciliation is ambiguous. The provider has no generic write or delete API.
