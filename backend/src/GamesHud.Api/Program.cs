using GamesHud.Api.Configuration;
using GamesHud.Api.Docker.Services;
using GamesHud.Api.GameServers.Definitions;
using GamesHud.Api.GameServers.Ports;
using GamesHud.Api.GameServers.Requirements;
using GamesHud.Api.GameServers.Services;
using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.HostCapabilities.Services;
using GamesHud.Api.Metrics.Configuration;
using GamesHud.Api.Metrics.Services;
using GamesHud.Api.Operations.Notifications;
using GamesHud.Api.Operations.Scheduling;
using GamesHud.Api.Palworld.Backups.Services;
using GamesHud.Api.Palworld.Configuration;
using GamesHud.Api.Palworld.Services;
using GamesHud.Api.Palworld.Updates.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DockerOptions>(builder.Configuration.GetSection(DockerOptions.SectionName));
builder.Services.Configure<PalworldOptions>(builder.Configuration.GetSection(PalworldOptions.SectionName));
builder.Services.Configure<MetricsOptions>(builder.Configuration.GetSection(MetricsOptions.SectionName));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.Configure<ScheduledOperationOptions>(builder.Configuration.GetSection(ScheduledOperationOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PalworldGameDefinition>();
builder.Services.AddSingleton<GameDefinition>(serviceProvider =>
    serviceProvider.GetRequiredService<PalworldGameDefinition>());
builder.Services.AddSingleton<IGameDefinitionRegistry, GameDefinitionRegistry>();
builder.Services.AddSingleton<IGameServerPlugin, PalworldGameServerPlugin>();
builder.Services.AddSingleton<IGameServerRegistry, GameServerRegistry>();
builder.Services.AddSingleton<IGameRequirementEvaluator, GameRequirementEvaluator>();
builder.Services.AddSingleton<IPortAllocator, PortAllocator>();
builder.Services.AddSingleton<IDockerPublishedPortProvider, DockerPublishedPortProvider>();
builder.Services.AddSingleton<IPortAvailabilityService, PortAvailabilityService>();
builder.Services.AddScoped<IPortPlanner, PortPlanner>();
builder.Services.AddSingleton<IManagedStoragePathBuilder, ManagedStoragePathBuilder>();
builder.Services.AddScoped<IGameStoragePlanner, GameStoragePlanner>();
builder.Services.AddScoped<IContainerService, DockerContainerService>();
builder.Services.AddSingleton<IHostMetricsFileSystem, HostMetricsFileSystem>();
builder.Services.AddSingleton<IHostSystemInfoProvider, RuntimeHostSystemInfoProvider>();
builder.Services.AddSingleton<LinuxHostMetricsService>();
builder.Services.AddSingleton<UnsupportedHostMetricsService>();
builder.Services.AddSingleton<IHostMetricsService>(serviceProvider =>
    serviceProvider.GetRequiredService<IHostSystemInfoProvider>().IsLinux
        ? serviceProvider.GetRequiredService<LinuxHostMetricsService>()
        : serviceProvider.GetRequiredService<UnsupportedHostMetricsService>());
builder.Services.AddSingleton<IHostSystemInspector, HostSystemInspector>();
builder.Services.AddSingleton<IHostStorageInfoProvider, RuntimeHostStorageInfoProvider>();
builder.Services.AddSingleton<IHostResourceInspector, HostResourceInspector>();
builder.Services.AddSingleton<IHostNetworkInfoProvider, RuntimeHostNetworkInfoProvider>();
builder.Services.AddSingleton<IHostNetworkInspector, HostNetworkInspector>();
builder.Services.AddScoped<IDockerRuntimeClient, DockerRuntimeClient>();
builder.Services.AddScoped<IHostRuntimeInspector, HostRuntimeInspector>();
builder.Services.AddScoped<IHostCapabilityService, HostCapabilityService>();
builder.Services.AddScoped<IDockerMetricsService, DockerMetricsService>();
builder.Services.AddScoped<IMetricsService, MetricsService>();
builder.Services.AddScoped<IPalworldMetricsService, PalworldMetricsService>();
builder.Services.AddSingleton<IMetricsHistoryStore, InMemoryMetricsHistoryStore>();
builder.Services.AddHostedService<MetricsSnapshotCollector>();
builder.Services.AddSingleton<IPalworldConfigFileSystem, PalworldConfigFileSystem>();
builder.Services.AddScoped<IPalworldConfigService, PalworldConfigService>();
builder.Services.AddScoped<IPalworldOverviewService, PalworldOverviewService>();
builder.Services.AddScoped<IPalworldAdminService, PalworldAdminService>();
builder.Services.AddScoped<IPalworldModsService, PalworldModsService>();
builder.Services.AddSingleton<PalworldBackupScheduleState>();
builder.Services.AddScoped<IPalworldBackupService, PalworldBackupService>();
builder.Services.AddHostedService<PalworldBackupScheduler>();
builder.Services.AddScoped<IPalworldContainerCommandService, DockerPalworldContainerCommandService>();
builder.Services.AddScoped<IPalworldUpdateRunner, PalworldUpdateOnBootRunner>();
builder.Services.AddScoped<IPalworldUpdateService, PalworldUpdateService>();
builder.Services.AddSingleton<NotificationRuntimeState>();
builder.Services.AddHttpClient<INotificationService, DiscordNotificationService>();
builder.Services.AddSingleton<IScheduleStore, InMemoryScheduleStore>();
builder.Services.AddScoped<IScheduledOperationExecutor, ScheduledOperationExecutor>();
builder.Services.AddScoped<ISchedulerService, SchedulerService>();
builder.Services.AddHostedService<OperationalScheduler>();
builder.Services
    .AddHttpClient<IPalworldRestService, PalworldRestService>()
    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddControllers();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("LocalVite", policy =>
        {
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalVite");
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapControllers();

app.Run();

public partial class Program;
