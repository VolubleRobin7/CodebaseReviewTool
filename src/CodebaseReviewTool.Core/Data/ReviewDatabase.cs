using CodebaseReviewTool.Core.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Core.Data;

/// <summary>Connection-string and first-run helpers shared by the runtime registration and the design-time factory.</summary>
public static class ReviewDatabase
{
    /// <summary>Number of steps the first-run seed creates, numbered from 0. The schema itself imposes no limit.</summary>
    public const int SeededFlowStepCount = 3;

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

    /// <summary>Applies pending migrations and seeds the three empty flow steps if they are missing.</summary>
    public static async Task MigrateAndSeedAsync(ReviewDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.MigrateAsync(cancellationToken);

        var existing = await db.FlowSteps.Select(f => f.Step).ToListAsync(cancellationToken);
        var missing = Enumerable.Range(0, SeededFlowStepCount).Except(existing).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        db.FlowSteps.AddRange(missing.Select(step => new FlowStep { Step = step, PromptText = string.Empty, UpdatedAt = now }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
