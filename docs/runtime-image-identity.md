# Durable Runtime Image Identity

ARCH-03 adds the persistence and provisioning contracts required to use an approved, immutable container image. GH-15 implements the narrowly scoped public-image acquisition mutation over that foundation. Neither feature deletes, prunes, tags, builds, signs or authenticates to a registry.

## Two Different Identities

A Managed runtime image has two identities with different purposes:

- The approved image is `registry/repository@sha256:<64 hex characters>` plus an explicit platform (`os`, `architecture` and optional variant). This is the immutable provisioning intent.
- The verified local image id is the Docker content id observed after acquisition, also represented as `sha256:<64 hex characters>`. GH-12 and GH-13 use this local id for V2 create, start and reconciliation.

The digest and local id are deliberately different types. A tag is not accepted as an approved V2 identity, and a local id cannot replace the approved registry digest in durable intent.

## Durable Intent

For `gh15-v2`, the reservation transaction creates the Managed server, operation, steps, ports, storage, game configuration and runtime image intent together. The intent records the exact operation/server/game/runtime owner, registry, repository, approved digest, platform and backend-controlled source.

Intent fields are immutable. The acquisition or trusted reconciliation boundary may make one optimistic transition from `pending` to `verified` by recording the verified local image id, timestamp and incremented version. Repeating the same observation is idempotent; a different local id, stale version or wrong owner fails closed. Normal acquisition requires `acquire_image` to be `Running`; reconciliation may record proven identity while that active step is `Running` or `Failed`. No image credentials, tokens or provider payloads are persisted.

## Versioned Pipelines

`gh09-v1` remains the fallback and keeps its original nine steps and metadata. Existing operations continue to use their persisted version and the separate legacy catalog selector; they do not require a V2 image intent.

`gh15-v2` is registered with `acquire_image` between `configure_game` and `create_runtime`. Mutation steps have bounded retry metadata so an effect proven absent may receive one controlled next-attempt authorization while capacity remains. A new Managed operation selects V2 only when its definition has exactly one supported pinned Docker image for the requested runtime. Definitions without that prerequisite remain on V1.

```text
gh09-v1: configure_game -> create_runtime -> start_runtime
gh15-v2: configure_game -> acquire_image -> create_runtime -> start_runtime
                                  |
                                  +-> verified local image id required downstream
```

V2 runtime reconstruction loads the approved image and verified local id from persistence. It does not replace historical image intent with a later catalog definition. Other runtime inputs, such as environment and resource requirements, still come from the current trusted game definition and remain a future versioning consideration.

## Approved Palworld Artifact

The Managed Palworld catalog entry records the human-approved release `v2.7.3` for `linux/amd64`:

- Registry: `docker.io`
- Repository: `thijsvanloef/palworld-server-docker`
- Approved platform manifest digest: `sha256:aee17c5ea7b52c0fdbc2f86c446c02bfdab8788eed3867ea7c34261874ab2ec9`
- OCI index digest, retained only as audit evidence: `sha256:be3ad49e373045a7b60478fd8b7f7411c1e293713dfa4733e563e798f276d688`

The release label is audit information in the backend-controlled approval source. The platform manifest digest is the runtime authority. Tags, including `latest`, are not consulted by V2 and cannot update the approved artifact. A future release requires a separate human approval and explicit catalog change; automatic update behavior is outside this flow.

## Trusted Acquisition

GH-15 performs this sequence using only the durable intent:

```text
inspect approved repository@digest
-> when proven absent, pull the same repository@digest and platform
-> inspect the immutable reference again
-> verify repository digest, platform and local image id
-> persist the local image id
-> complete acquire_image
```

A matching initial inspect skips the pull. `NotFound` is accepted only from a provider response that proves absence. Mismatch, malformed inspection and provider failure do not become absence. A normal Docker progress-stream completion is called `dispatched`, not success; only the final inspect can prove acquisition.

The Docker boundary passes no `AuthConfig`, reads no Docker credential file and supports public images only. Progress is processed with constant memory, provider text is reduced to safe classifications, and raw messages are neither logged nor persisted. Known digest rejection, unsupported authentication and disk exhaustion receive safe codes. Disk exhaustion never triggers cleanup.

Pull timeout is configured under `RuntimeImageAcquisition:TimeoutSeconds`, defaults to 900 seconds and is capped at 3600 seconds. Inspect timeout defaults to 30 seconds and is capped at 120 seconds. Cancellation before pull dispatch has no mutation effect; timeout, cancellation or connection loss after dispatch is unknown until reconciliation.

Crash recovery always inspects the same durable reference. A complete matching image can restore a missing verified local id; absence can authorize one controlled retry; partial, conflicting or unavailable state remains ambiguous. If an image disappears after acquisition, GH-12 fails closed on the missing verified local id target and does not pull or substitute another reference.

## Applied Reconciliation

The reconciliation application service accepts an operation, step and expected operation version. Callers cannot submit an outcome. The service invokes the registered trusted reconciler and persists the result and an append-only audit row in one transaction:

- `effect_exists` marks the step `Succeeded`; `acquire_image` also requires a durable verified local id.
- `effect_absent` records a transient failure and authorizes exactly the next attempt when `MaxAttempts` permits it.
- `ambiguous` records `Failed/unknown` and retains the server's active operation slot.

An authorized retry is consumed when the failed step returns to `Running`. Attempts remain bounded, earlier steps must be complete and optimistic operation concurrency rejects stale workers. Unknown mutation state retains the active slot even when the operation itself is `Failed`, preventing another operation from racing an unresolved external effect.

Explicit user cancellation and execution interruption have distinct intent. An explicit cancellation remains terminal. Request, shutdown or worker interruption after provider dispatch records an unknown mutation outcome for reconciliation and does not claim that the external effect was cancelled.

## Security Boundary

V2 fails closed unless the durable image intent is fully pinned and the local image id has been verified for the same owner. Docker create receives the verified local id and inspect must report the same id before adoption, start or reconciliation succeeds. The request contract still exposes no image, digest, platform, registry credentials or generic Docker options.

Images are shared host resources and operations do not claim exclusive ownership. Image deletion and cleanup are outside this flow. Registry authentication, private images, signature verification, capacity planning and update policy also remain separate work.
