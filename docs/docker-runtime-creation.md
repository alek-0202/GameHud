# Managed Docker Runtime Creation

Runtime port bindings carry separate `ContainerPort`, nullable `HostPort`, protocol and publication state. Docker `ExposedPorts` always uses the fixed game container port. Published endpoints bind their allocated host port to that key. Internal endpoints have no host port reservation and no `HostConfig.PortBindings` entry.

Runtime mount validation checks the prepared API-visible path while Docker receives the persisted daemon-visible host path. Neither path is accepted from client input.

GH-12 creates a stopped Managed Docker container from a validated runtime specification. It never pulls an image,
starts a container, or accepts raw Docker options. Identity, image, ports, mounts, resources, restart/network policy,
labels, and trusted non-secret environment are backend-controlled and validated before mapping to Docker SDK types.

ARCH-02 permits only environment entries supplied by the trusted `GameDefinition`. The Docker adapter serializes
the immutable approved set into `CreateContainerParameters.Env`. Critical reconciliation compares the full expected
environment internally; missing, modified, or extra entries are ambiguous drift. Environment contents are not logged,
and no GamesHud secret may travel through this mechanism.

Container creation remains create-only and daemon-free fakes cover the normal test suite. Pipeline `gh09-v1`,
persistence, LegacyExternal behavior, and all other Docker mutations are unchanged.
