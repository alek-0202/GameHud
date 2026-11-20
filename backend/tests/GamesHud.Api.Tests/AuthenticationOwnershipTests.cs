using System.Net;
using System.Net.Http.Json;
using GamesHud.Api.Authentication;
using GamesHud.Api.Configuration;
using GamesHud.Api.GameServers.Contracts;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace GamesHud.Api.Tests;

public sealed class AuthenticationOwnershipTests
{
    [Fact]
    public async Task LoginMeAndLogoutUseIdentityCookieAndAntiforgery()
    {
        using var directory = TemporaryDirectory.Create();
        await using var factory = CreateFactory(directory.Path);
        await CreateUser(factory, "alpha@example.test", "Alpha-Password-123!");
        using var client = factory.CreateClient();
        var csrf = await client.GetCsrfTokenAsync();

        using var badRequest = CreateLogin("missing@example.test", "Alpha-Password-123!", csrf);
        using var bad = await client.SendAsync(badRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal("invalid_credentials",
            (await bad.Content.ReadFromJsonAsync<AuthenticationErrorResponse>())?.Code);

        using var loginRequest = CreateLogin("alpha@example.test", "Alpha-Password-123!", csrf);
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.Contains("GamesHud.Auth", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<CurrentSessionResponse>("/api/auth/me");
        Assert.True(me?.Authenticated);
        Assert.Equal("alpha@example.test", me?.User?.Email);

        using var managedWithoutCsrf = await client.PostAsJsonAsync("/api/game-servers",
            new { gameId = "palworld", displayName = "Missing CSRF" });
        Assert.Equal(HttpStatusCode.BadRequest, managedWithoutCsrf.StatusCode);
        using var managedWithInvalidCsrf = new HttpRequestMessage(HttpMethod.Post, "/api/game-servers")
        { Content = JsonContent.Create(new { gameId = "palworld", displayName = "Invalid CSRF" }) };
        managedWithInvalidCsrf.Headers.Add("Idempotency-Key", "invalid-csrf");
        managedWithInvalidCsrf.AddCsrf("invalid-token");
        using var invalidManagedResponse = await client.SendAsync(managedWithInvalidCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidManagedResponse.StatusCode);

        using var logoutWithoutCsrf = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutCsrf.StatusCode);
        var logoutCsrf = await client.GetCsrfTokenAsync();
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.AddCsrf(logoutCsrf);
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False((await client.GetFromJsonAsync<CurrentSessionResponse>("/api/auth/me"))?.Authenticated);
    }

    [Fact]
    public async Task LoginRejectsMissingOrInvalidAntiforgeryAndRegistrationIsNotExposed()
    {
        using var directory = TemporaryDirectory.Create();
        await using var factory = CreateFactory(directory.Path);
        using var client = factory.CreateClient();
        using var missing = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@example.test", "Password-123!"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await client.GetCsrfTokenAsync();
        using var invalidRequest = CreateLogin("nobody@example.test", "Password-123!", "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/auth/register", new { email = "x@y.z", password = "x" })).StatusCode);
    }

    [Fact]
    public async Task AnonymousManagedEndpointsReturn401WithoutRedirect()
    {
        using var directory = TemporaryDirectory.Create();
        await using var factory = CreateFactory(directory.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var post = await client.PostAsJsonAsync("/api/game-servers", new { gameId = "palworld", displayName = "A" });
        using var get = await client.GetAsync("/api/game-servers/unknown");
        using var provisioning = await client.GetAsync("/api/game-servers/unknown/provisioning");
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, provisioning.StatusCode);
        Assert.Null(post.Headers.Location);
    }

    [Fact]
    public void PublicCreateContractCannotSpoofOwner()
    {
        var names = typeof(CreateManagedGameServerRequest).GetProperties()
            .Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Owner", StringComparison.OrdinalIgnoreCase)
            || name.Contains("User", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductionCookieIsSecureHttpOnlyAndDataProtectionUsesPersistentManagedPath()
    {
        using var directory = TemporaryDirectory.Create();
        using var factory = CreateFactory(directory.Path, "Production");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Strict, options.Cookie.SameSite);
        Assert.Equal(Path.Combine(directory.Path, "configured-keys"),
            factory.Services.GetRequiredService<IOptions<GamesHud.Api.Authentication.AuthenticationOptions>>()
                .Value.DataProtectionKeysPath);
    }

    [Fact]
    public async Task ApplicationRejectsCreationWithoutAuthenticatedOwner()
    {
        var service = new ManagedGameServerApplicationService(new NeverProvisioningService(), new AnonymousCurrentUser());
        var result = await service.CreateAsync(new("palworld", "Server"), "key", default);
        Assert.False(result.Succeeded);
        Assert.Equal("authentication_required", result.ErrorCode);
    }

    private static HttpRequestMessage CreateLogin(string email, string password, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        { Content = JsonContent.Create(new LoginRequest(email, password)) };
        request.AddCsrf(csrf);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(string root, string environment = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:DataRoot"] = root,
                    ["Storage:ManagedApiRoot"] = root,
                    ["Storage:ManagedHostRoot"] = root,
                    ["Authentication:DataProtectionKeysPath"] = Path.Combine(root, "configured-keys")
                }));
        });

    private static async Task CreateUser(WebApplicationFactory<Program> factory, string email, string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAccountProvisioningService>()
            .CreateAsync(email, password, default);
        Assert.True(result.Succeeded, string.Join(",", result.ErrorCodes));
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? UserId => null;
    }

    private sealed class NeverProvisioningService : GamesHud.Api.GameServers.Provisioning.IGameServerProvisioningService
    {
        public Task<GamesHud.Api.GameServers.Provisioning.IdempotentProvisioningExecutionResult> ScheduleIdempotentProvisioningAsync(
            GamesHud.Api.GameServers.Provisioning.CreateGameServerProvisioningRequest request, string ownerId,
            string key, string fingerprint, CancellationToken token) => throw new InvalidOperationException();
        public Task<GamesHud.Api.GameServers.Provisioning.ProvisioningPreviewResult> PreviewAsync(GamesHud.Api.GameServers.Provisioning.CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<GamesHud.Api.GameServers.Provisioning.ProvisioningExecutionResult> ScheduleProvisioningAsync(GamesHud.Api.GameServers.Provisioning.CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<GamesHud.Api.GameServers.Provisioning.ProvisioningExecutionResult> StartProvisioningAsync(GamesHud.Api.GameServers.Provisioning.CreateGameServerProvisioningRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<GamesHud.Api.GameServers.Provisioning.ProvisioningOperationSnapshot>> GetIncompleteOperationsAsync(CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) { Path = path; }
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-sec04-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path); return new(path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
