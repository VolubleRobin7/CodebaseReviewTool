using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>Where one step of a review is.</summary>
public sealed record ReviewStepStatus(int Step, StepRunState State, DateTime? StartedAt, DateTime? CompletedAt, long? DurationMs);

/// <summary>A review's state as read at one moment, without the step runs' prompts and output.</summary>
/// <param name="Review">The review row, untracked; its <see cref="Review.PullRequest"/> is loaded and its
/// <see cref="Review.StepRuns"/> hold only the columns <see cref="Review.State"/> needs.</param>
/// <param name="Steps">The review's step runs in step order.</param>
/// <param name="IssueCounts">Issues found, by severity; only severities with at least one issue appear.</param>
public sealed record ReviewStatus(Review Review, IReadOnlyList<ReviewStepStatus> Steps, IReadOnlyDictionary<IssueSeverity, int> IssueCounts)
{
    public ReviewState State => Review.State;

    /// <summary>True for the states a review only leaves when someone runs it again.</summary>
    public bool IsFinished => State is ReviewState.Completed or ReviewState.Failed or ReviewState.Cancelled;
}

/// <summary>Reads a review's status for callers outside the UI (the HTTP API).</summary>
public sealed class ReviewStatusReader(IDbContextFactory<ReviewDbContext> dbContextFactory)
{
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;

    /// <summary>The review's current status, or null when there is no such review.</summary>
    public async Task<ReviewStatus?> GetStatusAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.Reviews
            .AsNoTracking()
            .Include(r => r.PullRequest)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
        if (review is null)
        {
            return null;
        }

        // Only the columns the derived states need; a step run's prompt and raw output can be large.
        review.StepRuns = await db.ReviewStepRuns
            .AsNoTracking()
            .Where(s => s.ReviewId == reviewId)
            .OrderBy(s => s.Step)
            .Select(s => new ReviewStepRun
            {
                ReviewId = s.ReviewId,
                Step = s.Step,
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt,
                CancelledAt = s.CancelledAt,
                Error = s.Error,
                DurationMs = s.DurationMs,
            })
            .ToListAsync(cancellationToken);

        var counts = await db.Issues
            .Where(i => i.ReviewId == reviewId)
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Severity, g => g.Count, cancellationToken);

        var steps = review.StepRuns
            .Select(s => new ReviewStepStatus(s.Step, s.State, s.StartedAt, s.CompletedAt, s.DurationMs))
            .ToList();
        return new ReviewStatus(review, steps, counts);
    }
}
