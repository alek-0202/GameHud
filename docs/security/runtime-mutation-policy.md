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

GH-12 replaces the no-mutation adapter with a Docker.DotNet create-only adapter. The `create_runtime` step invokes policy before this adapter. Pipeline `gh09-v1` is unchanged because step identity, order and recovery semantics did not change. Unknown outcomes require real Docker reconciliation before retry.

GH-10 adds a central mutation executor after policy approval. The adapter now receives a typed execution context containing the validated specification and stable backend identities. Provider exceptions, unsupported results and cancellation after invocation are translated into safe unknown outcomes that require reconciliation. Cancellation observed before invocation does not call the adapter.

Resource checks use game minimum requirements when available and map only CPU count and memory. Before create, managed mount directories must exist and their ancestry must contain no reparse point. Docker socket sources and targets are rejected. Image acquisition, digest pinning/signing, registry authentication, and stronger provider-specific quota controls remain future work.

The provider request contains only the trusted image reference, deterministic name, four fixed ownership labels, definition-derived exposed ports, public reservation-derived bindings, Managed bind mounts, non-privileged default networking, approved resource limits, and `unless-stopped`. It contains no command, entrypoint, environment, device, capability, security option, host namespace, arbitrary label, or generic Docker option. `unless-stopped` does not start a newly created container; GH-12 makes no start call.

GH-11 storage preparation remains upstream of runtime creation: only directories represented by durable Managed storage reservations are created. Runtime mounts still consume those same approved reservations; filesystem preparation does not permit client paths or create mounts.

GH-13 reuses the validated specification and GH-12 identity/configuration proof before start. Only created/exited Managed runtimes may dispatch `StartContainerAsync`; running is idempotent, while foreign, paused, restarting, removing, dead, or drifted resources fail closed. Start accepts only the provider container ID obtained from the verified inspect flow and exposes no client/provider options. Health verification is a separate read-only bounded inspection and never escalates into lifecycle mutation.
