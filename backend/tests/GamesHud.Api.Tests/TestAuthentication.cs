using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GamesHud.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

internal static class TestAuthentication
{
    public const string Scheme = "GamesHud.Test";
    public const string UserHeader = "X-GamesHud-Test-User";

    public static void Add(IServiceCollection services)
    {
        services.PostConfigure<AntiforgeryOptions>(options =>
            options.Cookie.SecurePolicy = CookieSecurePolicy.None);
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
    }

    public static void AuthenticateAs(this HttpClient client, string userId) =>
        client.DefaultRequestHeaders.Add(UserHeader, userId);

    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/csrf");
        return response!.RequestToken;
    }

    public static void AddCsrf(this HttpRequestMessage request, string token) =>
        request.Headers.Add("X-CSRF-TOKEN", token);
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TestAuthentication.UserHeader, out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
            return Task.FromResult(AuthenticateResult.NoResult());
        var id = values.ToString().Trim();
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(ClaimTypes.Name, $"{id}@test.invalid"),
            new Claim(ClaimTypes.Email, $"{id}@test.invalid")
        ], TestAuthentication.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuthentication.Scheme)));
    }
}
