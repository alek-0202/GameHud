using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Domain;
using GamesHud.Api.GameServers.Provisioning;
using GamesHud.Api.GameServers.Runtime;
using GamesHud.Api.Secrets.Models;

namespace GamesHud.Api.Tests;

public sealed class RuntimeMutationPolicyTests
{
    private readonly RuntimeMutationPolicy _policy = new();
    private readonly PalworldGameDefinition _definition = new();

    [Fact]
    public void ValidSpecificationIsAllowed()
    {
        var root = CreateRoot();
        var result = _policy.Validate(CreateSpecification(root), _definition, root);
        Assert.True(result.Allowed);
        Assert.NotNull(result.Specification);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("podman", RuntimePolicyErrorCodes.UnknownRuntime)]
    [InlineData("docker", RuntimePolicyErrorCodes.UntrustedRuntimeImage)]
    public void UnsupportedRuntimeOrImageIsDenied(string runtime, string expectedCode)
    {
        var root = CreateRoot();
        var specification = CreateSpecification(root) with
        {
            RuntimeType = runtime,
            Image = expectedCode == RuntimePolicyErrorCodes.UntrustedRuntimeImage
                ? new TrustedRuntimeImage("docker", "untrusted/example", "latest", "test")
                : _definition.RuntimeImages.Single()
        };
        var result = _policy.Validate(specification, _definition, root);
        Assert.False(result.Allowed);
        Assert.Contains(result.Violations, item => item.Code == expectedCode);
    }

    [Fact]
    public void ArbitraryHostPathIsDeniedWithoutLeakingIt()
    {
        var root = CreateRoot();
        var secretPath = Path.GetFullPath(Path.Combine(root, "..", "private", "docker.sock"));
        var specification = CreateSpecification(root) with
        {
            Mounts = [new("storage", "data", secretPath, "/palworld", false)]
        };
        var result = _policy.Validate(specification, _definition, root);
        Assert.False(result.Allowed);
        Assert.DoesNotContain(secretPath, string.Join(' ', result.Violations.Select(item => item.SafeMessage)));
    }

    [Theory]
    [InlineData("tcp", "public")]
    [InlineData("udp", "internal")]
    public void ProtocolOrExposureMismatchIsDenied(string protocol, string exposure)
    {
        var root = CreateRoot();
        var specification = CreateSpecification(root) with
        {
            Ports = [new("port", "game", protocol, 8211, exposure)]
        };
        Assert.Contains(_policy.Validate(specification, _definition, root).Violations,
            item => item.Code == RuntimePolicyErrorCodes.PortReservationMismatch);
    }

    [Theory]
    [InlineData(0, 1024UL)]
    [InlineData(1, 0UL)]
    public void InvalidResourceLimitsAreDenied(int cpu, ulong memory)
    {
        var root = CreateRoot();
        var result = _policy.Validate(CreateSpecification(root) with { Resources = new(cpu, memory) }, _definition, root);
        Assert.Contains(result.Violations, item => item.Code == RuntimePolicyErrorCodes.ResourceLimitInvalid);
    }

    [Fact]
    public void AdapterAndClientContractsHaveNoProviderEscapeHatches()
    {
        var requestProperties = typeof(CreateGameServerProvisioningRequest).GetProperties().Select(item => item.Name).ToArray();
        Assert.DoesNotContain(requestProperties, name => new[] { "Image", "Command", "Mounts", "Environment", "Privileged", "Network", "Devices" }.Contains(name));
        var method = Assert.Single(typeof(IGameRuntimeAdapter).GetMethods());
        Assert.Equal(typeof(ValidatedRuntimeMutationSpecification), method.GetParameters()[0].ParameterType);
        Assert.Empty(typeof(ValidatedRuntimeMutationSpecification).GetConstructors());
        Assert.DoesNotContain(typeof(RuntimeMutationSpecification).GetProperties(), property =>
            property.Name is "Command" or "Entrypoint" or "Environment" or "Privileged" or "Devices" or "SecurityOptions");
    }

    [Fact]
    public void SpecificationAcceptsSecretReferencesButNotPlaintextSecretValues()
    {
        var root = CreateRoot();
        var reference = new SecretReference(SecretId.New());
        var specification = CreateSpecification(root) with { SecretReferences = [reference] };
        Assert.Equal(reference, Assert.Single(specification.SecretReferences));
        Assert.DoesNotContain(typeof(RuntimeMutationSpecification).GetProperties(), property => property.PropertyType == typeof(SecretValue));
    }

    private RuntimeMutationSpecification CreateSpecification(string root) => new(
        new GameServerId("test-server"), new GameId("palworld"), "operation", "docker",
        _definition.RuntimeImages.Single(),
        [new("port", "game", "udp", 8211, "public")],
        [new("storage", "data", Path.Combine(root, "servers", "test-server", "data"), "/palworld", false)],
        [], new RuntimeResourceLimits(1, 1024), RuntimeRestartPolicies.UnlessStopped, RuntimeNetworkPolicies.GamesHudManaged);

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "gameshud-sec03", Guid.NewGuid().ToString("N"));
}
