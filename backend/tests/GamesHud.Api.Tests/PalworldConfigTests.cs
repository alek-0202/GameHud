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
    public async Task GetConfigReturnsSchemaValuesAndDoesNotExposePasswords()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.GetConfigAsync(CancellationToken.None);

        Assert.Equal("palworld-server-new", result.ContainerName);
        Assert.Equal(PalworldSettingSchema.Settings.Count, result.Settings.Count);
        Assert.Equal("Existing Server", Setting(result, "ServerName").Value);
        Assert.Equal("1.5", Setting(result, "ExpRate").Value);
        Assert.Equal("True", Setting(result, "bIsPvP").Value);
        Assert.Equal("All", Setting(result, "DeathPenalty").Value);
        Assert.Equal("32", Setting(result, "ServerPlayerMaxNum").Value);
        Assert.Equal("Existing \"Quoted\" Server", Setting(result, "ServerDescription").Value);

        var serverPassword = Setting(result, "ServerPassword");
        var adminPassword = Setting(result, "AdminPassword");

        Assert.True(serverPassword.HasValue);
        Assert.Null(serverPassword.Value);
        Assert.True(serverPassword.SecuritySensitive);
        Assert.True(adminPassword.HasValue);
        Assert.Null(adminPassword.Value);
    }

    [Fact]
    public async Task UpdateConfigPreservesUnknownSettingsAndUpdatesMultipleTypes()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.UpdateConfigAsync(
            Update(
                ("ServerName", "Updated Server"),
                ("ServerDescription", "Updated \"Description\""),
                ("ExpRate", "2.25"),
                ("ServerPlayerMaxNum", "24"),
                ("bIsPvP", "false"),
                ("DeathPenalty", "None"),
                ("CrossplayPlatforms", "(Steam,Xbox)")),
            restart: false,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(directory.SettingsFile);

        Assert.Equal(7, result.ChangedSettings);
        Assert.False(result.LifecycleApplied);
        Assert.Contains("ServerName=\"Updated Server\"", text, StringComparison.Ordinal);
        Assert.Contains("ServerDescription=\"Updated \\\"Description\\\"\"", text, StringComparison.Ordinal);
        Assert.Contains("ExpRate=2.25", text, StringComparison.Ordinal);
        Assert.Contains("ServerPlayerMaxNum=24", text, StringComparison.Ordinal);
        Assert.Contains("bIsPvP=False", text, StringComparison.Ordinal);
        Assert.Contains("DeathPenalty=None", text, StringComparison.Ordinal);
        Assert.Contains("CrossplayPlatforms=\"(Steam,Xbox)\"", text, StringComparison.Ordinal);
        Assert.Contains("UnknownSetting=KeepMe", text, StringComparison.Ordinal);
        Assert.Contains("FuturePalworldSetting=PreserveMe", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyPasswordPreservesExistingPassword()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.UpdateConfigAsync(
            Update(("ServerPassword", string.Empty)),
            restart: false,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(directory.SettingsFile);

        Assert.Equal(0, result.ChangedSettings);
        Assert.Null(result.BackupFileName);
        Assert.Contains("ServerPassword=\"secret-value\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordValueIsWrittenButNeverReturned()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.UpdateConfigAsync(
            Update(("ServerPassword", "new secret")),
            restart: false,
            CancellationToken.None);
        var text = await File.ReadAllTextAsync(directory.SettingsFile);
        var passwordSetting = Setting(result.Config, "ServerPassword");

        Assert.Equal(1, result.ChangedSettings);
        Assert.Contains("ServerPassword=\"new secret\"", text, StringComparison.Ordinal);
        Assert.True(passwordSetting.HasValue);
        Assert.Null(passwordSetting.Value);
    }

    [Fact]
    public async Task NoOpUpdateDoesNotRewriteBackupOrRunLifecycle()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var originalText = await File.ReadAllTextAsync(directory.SettingsFile);
        var containerService = new RecordingContainerService();
        var service = CreateService(directory.Path, containerService);

        var result = await service.UpdateConfigAsync(
            Update(
                ("ExpRate", "1.500000"),
                ("ServerName", "Existing Server")),
            restart: true,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(directory.SettingsFile);
        var backups = Directory.GetFiles(directory.SettingsDirectory, "PalWorldSettings.ini.*.bak");

        Assert.Equal(0, result.ChangedSettings);
        Assert.False(result.LifecycleApplied);
        Assert.Null(result.BackupFileName);
        Assert.Equal(originalText, text);
        Assert.Empty(backups);
        Assert.Empty(containerService.StartRequests);
        Assert.Empty(containerService.StopRequests);
        Assert.Empty(containerService.RestartRequests);
    }

    [Fact]
    public async Task UpdateConfigCreatesBackupBeforeWriting()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var service = CreateService(directory.Path);

        var result = await service.UpdateConfigAsync(
            Update(("ServerName", "Updated Server")),
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
                Update(
                    ("UnknownSetting", "x"),
                    ("ExpRate", "101"),
                    ("DeathPenalty", "Invalid"),
                    ("ServerPlayerMaxNum", "0"),
                    ("bIsPvP", "maybe"),
                    ("ServerName", new string('a', 513))),
                restart: false,
                CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("UnknownSetting", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("ExpRate", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("DeathPenalty", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("ServerPlayerMaxNum", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("bIsPvP", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("ServerName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartFalseDoesNotCallLifecycle()
    {
        using var directory = TemporaryPalworldDirectory.CreateWithSettings();
        var containerService = new RecordingContainerService();
        var service = CreateService(directory.Path, containerService);

        await service.UpdateConfigAsync(
            Update(("ServerName", "Updated Server")),
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

        var result = await service.UpdateConfigAsync(
            Update(("ServerName", "Updated Server")),
            restart: true,
            CancellationToken.None);

        Assert.True(result.LifecycleApplied);
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
                Update(("ServerName", "Updated Server")),
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

        Assert.Contains("\"key\":\"ServerPassword\"", content, StringComparison.Ordinal);
        Assert.Contains("\"hasValue\":true", content, StringComparison.Ordinal);
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
            Update(("ExpRate", "-1")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static PalworldSettingResponse Setting(PalworldConfigResponse config, string key)
    {
        return config.Settings.Single(setting => setting.Key == key);
    }

    private static PalworldConfigUpdateRequest Update(params (string Key, string? Value)[] settings)
    {
        return new PalworldConfigUpdateRequest(
            settings
                .Select(setting => new PalworldSettingUpdateRequest(setting.Key, setting.Value))
                .ToArray());
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
            string stream,
            string? search,
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
            OptionSettings=(Difficulty=None,ServerName="Existing Server",ServerDescription="Existing \"Quoted\" Server",ServerPassword="secret-value",AdminPassword="admin-secret",ServerPlayerMaxNum=32,ExpRate=1.5,PlayerDamageRateAttack=1.0,PalCaptureRate=1.0,PlayerStomachDecreaceRate=1.0,PlayerStaminaDecreaceRate=1.0,WorkSpeedRate=1.0,CollectionDropRate=1.0,EnemyDropItemRate=1.0,PalEggDefaultHatchingTime=72.0,DeathPenalty=All,GuildPlayerMaxNum=20,BaseCampMaxNum=128,BaseCampWorkerMaxNum=15,bIsPvP=True,CrossplayPlatforms="(Steam,Xbox,PS5,Mac)",UnknownSetting=KeepMe,FuturePalworldSetting=PreserveMe)
            """;
    }
}
