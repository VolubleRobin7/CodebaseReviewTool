using CodebaseReviewTool.Application.Prompts;
using CodebaseReviewTool.Domain.Entities;
using CodebaseReviewTool.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Application.Data;

/// <summary>First-run work on the database, which needs the default prompts and so cannot live in Persistence.</summary>
public static class ReviewDatabaseSeeder
{
    /// <summary>
    /// Applies pending migrations and seeds a flow step for every shipped default prompt that has no row yet.
    /// Existing rows are never overwritten, so the user's edits survive restarts; the one exception is a step with
    /// no name, which gets the shipped one.
    /// </summary>
    public static async Task MigrateAndSeedAsync(ReviewDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.MigrateAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var existing = await db.FlowSteps.ToListAsync(cancellationToken);

        foreach (var step in existing.Where(s => string.IsNullOrWhiteSpace(s.Name)))
        {
            step.Name = DefaultPrompts.For(step.Step)?.Name ?? $"Step {step.Step + 1}";
            step.UpdatedAt = now;
        }

        var missing = DefaultPrompts.Steps.Except(existing.Select(s => s.Step)).Select(step => DefaultPrompts.For(step)!);
        db.FlowSteps.AddRange(missing.Select(d => new FlowStep { Step = d.Step, Name = d.Name, PromptText = d.Text, UpdatedAt = now }));

        await db.SaveChangesAsync(cancellationToken);
    }
}
