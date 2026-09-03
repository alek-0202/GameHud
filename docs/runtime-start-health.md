# Managed Runtime Start And Readiness

GH-13 preserves three separate provisioning effects:

```text
CreateRuntime -> container Created/Stopped
StartRuntime -> container Running
VerifyHealth -> generic runtime ready
```

`StartRuntime` is a closed typed mutation executed through the GH-10 boundary. The Docker adapter reconstructs the GH-12 deterministic name and labels from the validated specification, lists and inspects the candidate, verifies trusted image and critical configuration, and starts only a proven Managed container in `created` or `exited` state. A correctly owned running container is idempotent success. Paused, restarting, removing, dead, conflicting, foreign, or unprovable resources fail closed. The only new provider mutation is Docker.DotNet `StartContainerAsync`; no options, command, configuration changes, or other lifecycle operations are exposed.

After Docker accepts start, GamesHud inspects again and reports success only when the runtime is proven running. Any exception or cancellation after dispatch is an unknown outcome and is never retried internally. Reconciliation reports `effect_exists` for a correctly owned running runtime, `effect_absent` for a correctly owned created/exited runtime, and `ambiguous` for every other state or provider failure.

`VerifyHealth` is read-only. Running without a Docker HEALTHCHECK is generic runtime readiness. When HEALTHCHECK exists, `healthy` is ready, `starting` is polled, and `unhealthy` fails. Running is not automatically game-specific health; Palworld REST readiness remains future plugin work and LegacyExternal Palworld is not queried.

Polling defaults to a 60-second timeout and 2-second interval through `RuntimeHealth__TimeoutSeconds` and `RuntimeHealth__PollIntervalSeconds`. Timeout is capped at 600 seconds, interval at 30 seconds, and interval cannot exceed timeout. Timeout leaves the runtime running for inspection: there is no implicit stop, restart, remove, or compensation mutation.

The existing typed restart policy remains `unless-stopped`, derived from SEC-03. Docker may independently move a runtime between running, restarting, and exited; reconciliation proves current state and ownership rather than attributing every transition to the provisioning operation.
