# Trusted Runtime Environment

ARCH-02 provides a closed path for non-secret environment required by trusted runtime images:

```text
GameDefinition
-> trusted non-secret runtime environment
-> RuntimeSpecificationBuilder
-> SEC-03 exact validation
-> immutable ValidatedRuntimeMutationSpecification
-> Docker adapter KEY=value mapping
-> container
```

Clients cannot submit names, values, overrides, or arbitrary environment dictionaries. Entries are immutable
backend definitions scoped to a runtime. SEC-03 validates safe bounded names and values, rejects duplicates, and
requires the specification set to equal the trusted definition exactly. Only the Docker adapter knows Docker's
`KEY=value` representation.

Palworld declares one entry: `DISABLE_GENERATE_SETTINGS=true`. The trusted
`thijsvanloef/palworld-server-docker:latest` image otherwise regenerates `PalWorldSettings.ini`; the entry preserves
the future GamesHud-managed file at `Pal/Saved/Config/LinuxServer/PalWorldSettings.ini` within the existing
`/palworld` data mount.

Secrets are not runtime environment configuration. Passwords, tokens, keys, `SecretReference`, and `SecretValue`
cannot be represented by this model. Existing container environment is compared internally during reconciliation
and is never logged. Missing, changed, or extra entries are configuration drift and result in `ambiguous`; GamesHud
does not recreate, mutate, or adopt the container automatically. LegacyExternal containers are unaffected.

This work changes no pipeline step or persistence schema. Pipeline `gh09-v1` remains unchanged.
