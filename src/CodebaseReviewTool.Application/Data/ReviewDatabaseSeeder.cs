using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Prompts;
using CodebaseReviewTool.Domain.Entities;

namespace CodebaseReviewTool.Application.Data;

/// <summary>First-run data for a migrated database, which needs the default prompts and so lives here.</summary>
public static class ReviewDatabaseSeeder
{
    /// <summary>
    /// Seeds a flow step for every shipped default prompt that has no row yet. Existing rows are never overwritten, so
    /// the user's edits survive restarts; the one exception is a step with no name, which gets the shipped one. The
    /// database must already be migrated.
    /// </summary>
    public static async Task SeedAsync(IReviewDbContextFactory dbContextFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);

        await using var db = dbContextFactory.CreateDbContext();

        var now = DateTime.UtcNow;
        var existing = db.GetEntities<FlowStep>().ToList();

        foreach (var step in existing.Where(s => string.IsNullOrWhiteSpace(s.Name)))
        {
            step.Name = DefaultPrompts.For(step.Step)?.Name ?? $"Step {step.Step + 1}";
            step.UpdatedAt = now;
        }

        var missing = DefaultPrompts.Steps.Except(existing.Select(s => s.Step)).Select(step => DefaultPrompts.For(step)!);
        foreach (var prompt in missing)
        {
            db.Add(new FlowStep { Step = prompt.Step, Name = prompt.Name, PromptText = prompt.Text, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
