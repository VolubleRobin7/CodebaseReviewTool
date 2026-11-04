using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Prompts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Core.Data;

/// <summary>Connection-string and first-run helpers shared by the runtime registration and the design-time factory.</summary>
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

    /// <summary>
    /// Applies pending migrations and seeds a flow step for every shipped default prompt that has no row yet.
    /// Existing rows are never overwritten, so the user's edits survive restarts.
    /// </summary>
    public static async Task MigrateAndSeedAsync(ReviewDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.MigrateAsync(cancellationToken);

        var existing = await db.FlowSteps.Select(f => f.Step).ToListAsync(cancellationToken);
        var missing = DefaultPrompts.Steps.Except(existing).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        db.FlowSteps.AddRange(missing.Select(step => new FlowStep { Step = step, PromptText = DefaultPrompts.For(step)!, UpdatedAt = now }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
