# Managed Storage Preparation

GH-11 introduces the first controlled provisioning side effect: creating directories for durable `Managed` storage reservations below the configured GamesHud `DataRoot`.

```text
ValidatedProvisioningPlan
-> durable StorageReservation
-> ValidatedManagedStorageTarget
-> IManagedStorageProvider.PrepareAsync
-> Directory.CreateDirectory
-> provisioning checkpoint
```

The target builder reloads durable state and requires the same game server, operation, storage definition, planned relative path, `reserved` status and `Managed` ownership. The expected layout is `servers/<GameServerId>/<StorageDefinitionId>`. The provider cannot accept an arbitrary string path; it accepts only the validated target.

Containment is enforced with the existing managed path builder. Existing path components are inspected for symbolic links/reparse points before and after creation, including configured-root ancestors. Inspection failure is fail-closed. This reduces symlink/junction escape risk, but filesystem checks and creation cannot be atomic across platforms; a privileged concurrent attacker can still create a TOCTOU race. Stronger handle-based platform-specific confinement remains future hardening.

Preparation is idempotent. An existing directory is accepted only in the context of the exact durable Managed reservation. GamesHud does not scan for or adopt unrelated directories. Directory permissions use process/OS defaults; GH-11 performs no chmod, chown or ACL mutation.

Reconciliation uses the existing GH-09 outcomes: all expected safe directories present is `effect_exists`, all absent is `effect_absent`, and mixed, inaccessible or unsafe state is `ambiguous`. A crash after creation but before checkpoint therefore routes to reconciliation instead of blind retry.

Cancellation checked before each local create guarantees that already-observed cancellation creates nothing further. `Directory.CreateDirectory` is a short synchronous OS operation and cannot be cancelled after dispatch; if cancellation races with it, durable `Running` state and reconciliation determine the effect.

Compensation is intentionally non-destructive. GH-11 never deletes directories because current persistence cannot prove that a directory was newly created by this attempt or remained empty and untouched. Recursive deletion is not exposed by the provider.

Tests use unique temporary roots only. GH-11 creates no file, Docker volume, bind mount, container, network, game installation or Palworld resource.
