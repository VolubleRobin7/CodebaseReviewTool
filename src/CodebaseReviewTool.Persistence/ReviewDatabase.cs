using Microsoft.Data.Sqlite;

namespace CodebaseReviewTool.Persistence;

/// <summary>
/// The connection string shared by the runtime registration and the design-time factory. Migrating and seeding is in
/// Application's <c>ReviewDatabaseSeeder</c>, since the seed comes from the default prompts there.
/// </summary>
public static class ReviewDatabase
{

    /// <summary>Builds the SQLite connection string for <paramref name="databasePath"/>, creating its directory if missing.</summary>
    public static string ConnectionString(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new SqliteConnectionStringBuilder { DataSource = fullPath, ForeignKeys = true }.ToString();
    }
}
