# Runtime Mutation Policy

SEC-03 establishes the internal boundary for future runtime creation. It performs no Docker, filesystem, network, firewall or game mutation.

```text
CreateGameServerProvisioningRequest
-> ValidatedProvisioningPlan
-> durable managed reservations
-> RuntimeMutationSpecification
-> deny-by-default IRuntimeMutationPolicy
-> ValidatedRuntimeMutationSpecification
-> IGameRuntimeAdapter
```

The request is not a runtime specification. Only backend-controlled `GameDefinition` metadata and reservations owned by the same managed server and provisioning operation can produce mounts and port bindings. The runtime image is selected from the definition, never from client input.

The specification contains typed ports, managed mounts, opaque `SecretReference` values, positive CPU/memory limits and closed restart/network policies. It has no arbitrary command, entrypoint, environment dictionary, privileged flag, host namespace, device, capability or security-option surface. The validated wrapper has no public constructor, and the adapter accepts only that wrapper.

Mount sources are reconstructed below the configured managed data root and checked for containment. Known engine and system paths are denied as defense in depth. No directory is created. Port bindings must correspond to persisted `reserved` reservations and to their game definitions; internal exposure cannot become public.

The current adapter deliberately reports that no mutation was executed. The `create_runtime` step invokes policy before this adapter. Pipeline `gh09-v1` is unchanged because step identity, order and recovery semantics did not change. A future real adapter remains a mutation step: unknown outcomes require reconciliation before retry.

Resource checks are currently structural and use game minimum requirements when available. Host-wide quotas, image pinning/signing, registry authentication, symlink/race hardening and a real runtime reconciler remain future work.
