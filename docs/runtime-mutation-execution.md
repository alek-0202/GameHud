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

No timeout default is introduced because no real provider call exists. A future provider must use a bounded, configurable timeout and treat expiry after dispatch as an unknown outcome.
