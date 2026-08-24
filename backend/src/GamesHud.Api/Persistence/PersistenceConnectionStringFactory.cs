using Microsoft.Data.Sqlite;

namespace GamesHud.Api.Persistence;

public static class PersistenceConnectionStringFactory
{
    public static string CreateSqliteConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = false,
        };

        return builder.ToString();
    }
}
