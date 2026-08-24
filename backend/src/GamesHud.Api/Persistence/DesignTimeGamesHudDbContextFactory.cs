using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GamesHud.Api.Persistence;

public sealed class DesignTimeGamesHudDbContextFactory : IDesignTimeDbContextFactory<GamesHudDbContext>
{
    public GamesHudDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(
            AppContext.BaseDirectory,
            "gameshud-data",
            "system",
            "gameshud.db");
        var options = new DbContextOptionsBuilder<GamesHudDbContext>()
            .UseSqlite(PersistenceConnectionStringFactory.CreateSqliteConnectionString(databasePath))
            .Options;

        return new GamesHudDbContext(options);
    }
}
