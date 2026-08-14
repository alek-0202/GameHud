using System.Net;
using System.Net.Http.Json;
using GamesHud.Api.Docker.Contracts;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Contracts;
using GamesHud.Api.Palworld.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GamesHud.Api.Tests;

public sealed class PalworldConfigTests
{
    [Fact]
    public async Task GetConfigParsesSupportedSettingsAndDoesNotExposePassword()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.GetConfigAsync(CancellationToken.None);

        Assert.Equal("palworld-server-new", result.ContainerName);
        Assert.Equal("Existing Server", result.ServerName);
        Assert.True(result.HasServerPassword);
        Assert.Equal(1.5m, result.ExpRate);
        Assert.Equal("All", result.DeathPenalty);
    }

    [Fact]
    public async Task UpdateConfigPreservesUnknownSettingsAndUpdatesMultipleProperties()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        await service.UpdateConfigAsync(
            new PalworldConfigUpdateRequest(
                "Updated Server",
                null,
                2.25m,
                null,
                3.5m,
                null,
                null,
                null,
                null,
                null,
                null,
                "None",
                32,
                null,
                null),
            restart: false,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(directory.SettingsFile);

        Assert.Contains("ServerName=\"Updated Server\"", text, StringComparison.Ordinal);
        Assert.Contains("ExpRate=2.25", text, StringComparison.Ordinal);
        Assert.Contains("PalCaptureRate=3.5", text, StringComparison.Ordinal);
        Assert.Contains("DeathPenalty=None", text, StringComparison.Ordinal);
        Assert.Contains("GuildPlayerMaxNum=32", text, StringComparison.Ordinal);
        Assert.Contains("UnknownSetting=KeepMe", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyPasswordPreservesExistingPassword()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        await service.UpdateConfigAsync(
            new PalworldConfigUpdateRequest(
                null,
                string.Empty,
                2m,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            restart: false,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(directory.SettingsFile);

        Assert.Contains("ServerPassword=\"secret-value\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateConfigCreatesBackupBeforeWriting()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.UpdateConfigAsync(
            new PalworldConfigUpdateRequest(
                "Updated Server",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            restart: false,
            CancellationToken.None);

        var backups = Directory.GetFiles(directory.SettingsDirectory, "PalWorldSettings.ini.*.bak");

        Assert.Single(backups);
        Assert.Equal(Path.GetFileName(backups[0]), result.BackupFileName);
    }

    [Fact]
    public async Task MissingSettingsFileThrowsFriendlyException()
    {
        using var directory = TemporaryPalworldDirectory.CreateEmpty();
        var service = CreateService(directory.Path);

        var exception = await Assert.ThrowsAsync<PalworldConfigNotFoundException>(
            () => service.GetConfigAsync(CancellationToken.None));

        Assert.Contains("PalWorldSettings.ini", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidValuesThrowValidationException()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var exception = await Assert.ThrowsAsync<PalworldConfigValidationException>(
            () => service.UpdateConfigAsync(
                new PalworldConfigUpdateRequest(
                    "",
                    null,
                    -1m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Invalid",
                    0,
                    null,
                    null),
                restart: false,
                CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("ServerName", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("ExpRate", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("DeathPenalty", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("GuildPlayerMaxNum", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartFalseDoesNotCallLifecycle()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var containerService = new RecordingContainerService();
        var service = CreateService(directory.Path, containerService);

        await service.UpdateConfigAsync(
            new PalworldConfigUpdateRequest(
                "Updated Server",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            restart: false,
            CancellationToken.None);

        Assert.Empty(containerService.StartRequests);
        Assert.Empty(containerService.StopRequests);
        Assert.Empty(containerService.RestartRequests);
    }

    [Fact]
    public async Task RestartTrueStopsAndStartsOnlyConfiguredContainer()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var containerService = new RecordingContainerService();
        var service = CreateService(directory.Path, containerService);

        await service.UpdateConfigAsync(
            new PalworldConfigUpdateRequest(
                "Updated Server",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            restart: true,
            CancellationToken.None);

        Assert.Equal(new[] { "palworld-server-new" }, containerService.StopRequests);
        Assert.Equal(new[] { "palworld-server-new" }, containerService.StartRequests);
        Assert.Empty(containerService.RestartRequests);
    }

    [Fact]
    public async Task WriteFailureRestoresBackupAndDoesNotStartContainer()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var originalText = await File.ReadAllTextAsync(directory.SettingsFile);
        var containerService = new RecordingContainerService();
        var service = CreateService(
            directory.Path,
            containerService,
            new FailingWriteFileSystem());

        await Assert.ThrowsAsync<PalworldConfigWriteException>(
            () => service.UpdateConfigAsync(
                new PalworldConfigUpdateRequest(
                    "Updated Server",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                restart: true,
                CancellationToken.None));

        var textAfterFailure = await File.ReadAllTextAsync(directory.SettingsFile);

        Assert.Equal(originalText, textAfterFailure);
        Assert.Equal(new[] { "palworld-server-new" }, containerService.StopRequests);
        Assert.Empty(containerService.StartRequests);
    }

    [Fact]
    public async Task GetEndpointDoesNotReturnPlaintextPassword()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        await using var factory = CreateFactory(directory.Path, new RecordingContainerService());
        using var client = factory.CreateClient();

        var content = await client.GetStringAsync("/api/palworld/config");

        Assert.Contains("\"hasServerPassword\":true", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"serverPassword\":", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PutEndpointReturnsBadRequestForInvalidValues()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        await using var factory = CreateFactory(directory.Path, new RecordingContainerService());
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            "/api/palworld/config",
            new PalworldConfigUpdateRequest(
                "",
                null,
                -1m,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "Invalid",
                null,
                null,
                null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static PalworldConfigService CreateService(
        string managedPath,
        RecordingContainerService? containerService = null,
        IPalworldConfigFileSystem? fileSystem = null)
    {
        return new PalworldConfigService(
            Options.Create(new PalworldOptions
            {
                ManagedPath = managedPath,
                ContainerName = "palworld-server-new"
            }),
            fileSystem ?? new PalworldConfigFileSystem(),
            containerService ?? new RecordingContainerService(),
            NullLogger<PalworldConfigService>.Instance);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string managedPath,
        RecordingContainerService containerService)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.Configure<PalworldOptions>(options =>
                    {
                        options.ManagedPath = managedPath;
                        options.ContainerName = "palworld-server-new";
                    });
                    services.AddSingleton<IContainerService>(containerService);
                });
            });
    }

    private sealed class TemporaryPalworldDirectory : IDisposable
    {
        private TemporaryPalworldDirectory(string path, string settingsDirectory, string settingsFile)
        {
            Path = path;
            SettingsDirectory = settingsDirectory;
            SettingsFile = settingsFile;
        }

        public string Path { get; }

        public string SettingsDirectory { get; }

        public string SettingsFile { get; }

        public static TemporaryPalworldDirectory CreateWithSettings()
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"gameshud-palworld-{Guid.NewGuid():N}");
            var settingsDirectory = System.IO.Path.Combine(
                root,
                "Pal",
                "Saved",
                "Config",
                "LinuxServer");
            var settingsFile = System.IO.Path.Combine(settingsDirectory, "PalWorldSettings.ini");

            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(settingsFile, CreateSettingsText());

            return new TemporaryPalworldDirectory(root, settingsDirectory, settingsFile);
        }

        public static TemporaryPalworldDirectory CreateEmpty()
        {
            var root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"gameshud-palworld-{Guid.NewGuid():N}");

            Directory.CreateDirectory(root);

            return new TemporaryPalworldDirectory(root, root, System.IO.Path.Combine(root, "PalWorldSettings.ini"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingContainerService : IContainerService
    {
        public List<string> StartRequests { get; } = new();

        public List<string> StopRequests { get; } = new();

        public List<string> RestartRequests { get; } = new();

        public Task<IReadOnlyCollection<ContainerResponse>> GetContainersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<ContainerResponse>>(Array.Empty<ContainerResponse>());
        }

        public Task<ContainerDetailsResponse?> GetContainerDetailsAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerDetailsResponse?>(null);
        }

        public Task<ContainerLogsResponse?> GetContainerLogsAsync(
            string containerId,
            int tail,
            bool timestamps,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ContainerLogsResponse?>(null);
        }

        public Task<ContainerLifecycleActionResponse?> StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            StartRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "start",
                "exited",
                "running"));
        }

        public Task<ContainerLifecycleActionResponse?> StopContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            StopRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "stop",
                "running",
                "exited"));
        }

        public Task<ContainerLifecycleActionResponse?> RestartContainerAsync(
            string containerId,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            RestartRequests.Add(containerId);

            return Task.FromResult<ContainerLifecycleActionResponse?>(CreateLifecycleResponse(
                containerId,
                "restart",
                "running",
                "running"));
        }
    }

    private sealed class FailingWriteFileSystem : IPalworldConfigFileSystem
    {
        private readonly PalworldConfigFileSystem _inner = new();

        public bool DirectoryExists(string path)
        {
            return _inner.DirectoryExists(path);
        }

        public IEnumerable<string> EnumerateFiles(
            string path,
            string searchPattern,
            SearchOption searchOption)
        {
            return _inner.EnumerateFiles(path, searchPattern, searchOption);
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            return _inner.ReadAllTextAsync(path, cancellationToken);
        }

        public Task WriteAllTextAsync(
            string path,
            string contents,
            CancellationToken cancellationToken)
        {
            throw new IOException("Expected test write failure.");
        }

        public void Copy(string sourceFileName, string destFileName, bool overwrite)
        {
            _inner.Copy(sourceFileName, destFileName, overwrite);
        }

        public void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            _inner.Move(sourceFileName, destFileName, overwrite);
        }

        public void DeleteIfExists(string path)
        {
            _inner.DeleteIfExists(path);
        }
    }

    private static ContainerLifecycleActionResponse CreateLifecycleResponse(
        string containerId,
        string action,
        string previousState,
        string currentState)
    {
        return new ContainerLifecycleActionResponse(
            containerId,
            action,
            true,
            "Lifecycle action completed.",
            previousState,
            currentState,
            "2026-01-02T03:04:05Z");
    }

    private static string CreateSettingsText()
    {
        return """
            [/Script/Pal.PalGameWorldSettings]
            OptionSettings=(Difficulty=None,ServerName="Existing Server",ServerPassword="secret-value",ExpRate=1.5,PlayerDamageRateAttack=1.0,PalCaptureRate=1.0,PlayerStomachDecreaceRate=1.0,PlayerStaminaDecreaceRate=1.0,WorkSpeedRate=1.0,CollectionDropRate=1.0,EnemyDropItemRate=1.0,PalEggDefaultHatchingTime=72.0,DeathPenalty=All,GuildPlayerMaxNum=20,BaseCampMaxNum=128,BaseCampWorkerMaxNum=15,UnknownSetting=KeepMe)
            """;
    }
}
