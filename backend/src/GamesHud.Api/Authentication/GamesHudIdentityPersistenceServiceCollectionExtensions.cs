using GamesHud.Api.GameServers.Storage;
using GamesHud.Api.Persistence;
using GamesHud.Api.Persistence.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GamesHud.Api.Authentication;

public static class GamesHudIdentityPersistenceServiceCollectionExtensions
{
    public static IdentityBuilder AddGamesHudIdentityPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
        services.AddSingleton<IPersistenceLayoutResolver, PersistenceLayoutResolver>();
        services.AddDbContext<GamesHudDbContext>((serviceProvider, options) =>
        {
            var layout = serviceProvider.GetRequiredService<IPersistenceLayoutResolver>().ResolveLayout();
            options.UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(layout.DatabasePath));
        });
        var identity = services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
            .AddEntityFrameworkStores<GamesHudDbContext>();
        services.AddScoped<IAccountProvisioningService, AccountProvisioningService>();
        services.AddScoped<IFirstAccountBootstrapService, FirstAccountBootstrapService>();
        services.AddScoped<IPersistenceInitializer, PersistenceInitializer>();
        return identity;
    }
}
