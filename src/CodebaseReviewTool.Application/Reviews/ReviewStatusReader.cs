using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Domain.Entities;
using CodebaseReviewTool.Domain.Enums;

namespace CodebaseReviewTool.Application.Reviews;

/// <summary>Where one step of a review is.</summary>
public sealed record ReviewStepStatus(int Step, StepRunState State, DateTime? StartedAt, DateTime? CompletedAt, long? DurationMs);

/// <summary>A review's state as read at one moment, without the step runs' prompts and output.</summary>
/// <param name="Review">The review row; its <see cref="Review.PullRequest"/> is loaded and its
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
public sealed class ReviewStatusReader(IReviewDbContextFactory dbContextFactory)
{
    private readonly IReviewDbContextFactory _dbContextFactory = dbContextFactory;

    /// <summary>The review's current status, or null when there is no such review.</summary>
    public async Task<ReviewStatus?> GetStatusAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var db = _dbContextFactory.CreateDbContext();

        var row = db.GetEntities<Review>()
            .Where(r => r.Id == reviewId)
            .Select(r => new { Review = r, r.PullRequest })
            .FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var review = row.Review;
        review.PullRequest = row.PullRequest;

        // Only the columns the derived states need; a step run's prompt and raw output can be large.
        review.StepRuns = db.GetEntities<ReviewStepRun>()
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
            .ToList();

        var counts = db.GetEntities<Issue>()
            .Where(i => i.ReviewId == reviewId)
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToDictionary(g => g.Severity, g => g.Count);

        var steps = review.StepRuns
            .Select(s => new ReviewStepStatus(s.Step, s.State, s.StartedAt, s.CompletedAt, s.DurationMs))
            .ToList();
        return new ReviewStatus(review, steps, counts);
    }
}
