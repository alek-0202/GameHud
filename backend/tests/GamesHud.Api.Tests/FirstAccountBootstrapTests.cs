using GamesHud.Api.Authentication;
using GamesHud.Api.Persistence;
using GamesHud.Bootstrap;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GamesHud.Api.Tests;

public sealed class FirstAccountBootstrapTests
{
    [Fact]
    public async Task FirstAccountUsesConfiguredIdentityAndIsPersisted()
    {
        using var directory = TemporaryDirectory.Create();
        await using var provider = await CreateProviderAsync(directory.Path);
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IFirstAccountBootstrapService>()
            .CreateFirstAsync("admin@example.test", "Strong-Password-123!", CancellationToken.None);

        Assert.Equal(FirstAccountBootstrapStatuses.Created, result.Status);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("admin@example.test");
        Assert.NotNull(user);
        Assert.Equal("admin@example.test", user.UserName);
        Assert.True(await users.CheckPasswordAsync(user, "Strong-Password-123!"));
        Assert.DoesNotContain("Strong-Password-123!", user.PasswordHash ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondBootstrapIsRefusedWithoutAddingAnotherUser()
    {
        using var directory = TemporaryDirectory.Create();
        await using var provider = await CreateProviderAsync(directory.Path);
        await using var scope = provider.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IFirstAccountBootstrapService>();
        Assert.True((await bootstrap.CreateFirstAsync(
            "first@example.test", "First-Password-123!", CancellationToken.None)).Succeeded);

        var second = await bootstrap.CreateFirstAsync(
            "second@example.test", "Second-Password-123!", CancellationToken.None);

        Assert.Equal(FirstAccountBootstrapStatuses.AlreadyInitialized, second.Status);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.CountAsync());
    }

    [Fact]
    public async Task InvalidPasswordUsesIdentityValidationAndLeavesDatabaseEmpty()
    {
        using var directory = TemporaryDirectory.Create();
        await using var provider = await CreateProviderAsync(directory.Path);
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IFirstAccountBootstrapService>()
            .CreateFirstAsync("admin@example.test", "short", CancellationToken.None);

        Assert.Equal(FirstAccountBootstrapStatuses.ValidationFailed, result.Status);
        Assert.NotEmpty(result.ErrorCodes);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.CountAsync());
    }

    [Fact]
    public async Task ConcurrentBootstrapCreatesExactlyOneAccount()
    {
        using var directory = TemporaryDirectory.Create();
        await using var provider = await CreateProviderAsync(directory.Path);
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var attempts = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<IFirstAccountBootstrapService>()
                .CreateFirstAsync("first@example.test", "First-Password-123!", CancellationToken.None),
            secondScope.ServiceProvider.GetRequiredService<IFirstAccountBootstrapService>()
                .CreateFirstAsync("second@example.test", "Second-Password-123!", CancellationToken.None));

        Assert.Single(attempts, result => result.Status == FirstAccountBootstrapStatuses.Created);
        Assert.Single(attempts, result => result.Status == FirstAccountBootstrapStatuses.AlreadyInitialized);
        await using var verification = provider.CreateAsyncScope();
        Assert.Equal(1, await verification.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>().Users.CountAsync());
    }

    [Fact]
    public async Task CommandSanitizesUnexpectedFailureAndDoesNotEchoCredentials()
    {
        var console = new FakeConsole("admin@example.test", "Strong-Password-123!", "Strong-Password-123!");
        var command = new FirstAccountBootstrapCommand(new ThrowingBootstrap(), console);

        var exitCode = await command.RunAsync(CancellationToken.None);

        Assert.Equal(BootstrapExitCodes.Failure, exitCode);
        var output = string.Join(Environment.NewLine, console.Output);
        Assert.DoesNotContain("admin@example.test", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Strong-Password-123!", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandRejectsRedirectedInputBeforeReadingCredentials()
    {
        var console = new FakeConsole(null, null, null) { IsInputRedirected = true };
        var command = new FirstAccountBootstrapCommand(new ThrowingBootstrap(), console);

        Assert.Equal(BootstrapExitCodes.Usage, await command.RunAsync(CancellationToken.None));
        Assert.Empty(console.Prompts);
    }

    private static async Task<ServiceProvider> CreateProviderAsync(string dataRoot)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Storage:DataRoot"] = dataRoot,
                ["Persistence:AutoMigrate"] = "true"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGamesHudIdentityPersistence(configuration);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPersistenceInitializer>()
            .InitializeAsync(CancellationToken.None);
        return provider;
    }

    private sealed class ThrowingBootstrap : IFirstAccountBootstrapService
    {
        public Task<FirstAccountBootstrapResult> CreateFirstAsync(
            string email,
            string password,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Data Source=/private/gameshud.db; secret password value");
    }

    private sealed class FakeConsole(string? line, string? password, string? confirmation) : IBootstrapConsole
    {
        private readonly Queue<string?> _secrets = new([password, confirmation]);
        public bool IsInputRedirected { get; set; }
        public List<string> Output { get; } = [];
        public List<string> Prompts { get; } = [];

        public string? ReadLine(string prompt)
        {
            Prompts.Add(prompt);
            return line;
        }

        public string ReadSecret(string prompt)
        {
            Prompts.Add(prompt);
            return _secrets.Dequeue() ?? string.Empty;
        }

        public void WriteLine(string message) => Output.Add(message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gameshud-sec04a-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
