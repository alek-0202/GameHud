using System.Net;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Authentication;

public static class PrivateHttpAccessPolicy
{
    public const string UnsafeConfigurationMessage =
        "Private HTTP authentication requires AllowedHosts to contain only explicit loopback hosts.";

    public static bool HasOnlyExplicitLoopbackHosts(string? allowedHosts)
    {
        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return hosts.Length > 0 && hosts.All(IsLoopbackHost);
    }

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var normalized = host.Trim().TrimStart('[').TrimEnd(']');
        return string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address);
    }
}

public sealed class PrivateHttpAuthenticationOptionsValidator(IConfiguration configuration)
    : IValidateOptions<AuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options) =>
        !options.PrivateHttpEnabled
        || PrivateHttpAccessPolicy.HasOnlyExplicitLoopbackHosts(configuration["AllowedHosts"])
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(PrivateHttpAccessPolicy.UnsafeConfigurationMessage);
}
