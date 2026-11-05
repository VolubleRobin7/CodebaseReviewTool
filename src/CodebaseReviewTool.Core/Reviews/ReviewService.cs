using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>Starts reviews: creates the review row, queues it and announces it.</summary>
public sealed class ReviewService(
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    ReviewQueue queue,
    ReviewEvents events,
    ILogger<ReviewService> logger) : IDisposable
{
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly ReviewQueue _queue = queue;
    private readonly ReviewEvents _events = events;
    private readonly ILogger<ReviewService> _logger = logger;

    // Serialises the "already reviewing?" check with the insert, so a double click cannot queue two reviews.
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <summary>
    /// Creates a <see cref="ReviewState.Queued"/> review of the cached pull request, queues it and raises
    /// <see cref="ReviewEvents.ReviewChanged"/>. Throws <see cref="InvalidOperationException"/> when the pull request is
    /// not in the cache or already has a review that is queued, preparing its worktree or running.
    /// </summary>
    public async Task<Review> StartReviewAsync(int pullRequestId, string repositoryKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);

        Review review;
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var pullRequest = await db.PullRequests.FindAsync([pullRequestId, repositoryKey], cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Pull request #{pullRequestId} in '{repositoryKey}' is not in the cache; wait for the next poll.");

            // State is derived, so load the reviews that could still be in flight (no error, not completed) with
            // their step runs and ask each one.
            var unfinished = await db.Reviews
                .Where(r => r.PullRequestId == pullRequestId && r.RepositoryKey == repositoryKey && r.Error == null && r.CompletedAt == null)
                .Include(r => r.StepRuns)
                .ToListAsync(cancellationToken);

            if (unfinished.FirstOrDefault(r => r.State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running) is { } active)
            {
                throw new InvalidOperationException(
                    $"Pull request #{pullRequestId} is already being reviewed (review {active.Id} is {Describe(active.State)}).");
            }

            review = new Review
            {
                PullRequestId = pullRequestId,
                RepositoryKey = repositoryKey,
                SourceCommit = pullRequest.SourceCommit,
                MergeBaseCommit = string.Empty,
                WorktreePath = string.Empty,
                QueuedAt = DateTime.UtcNow,
            };
            db.Reviews.Add(review);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _startGate.Release();
        }

        _queue.Enqueue(review.Id);
        _logger.LogInformation("Queued review {ReviewId} of pull request #{PullRequestId} in {RepositoryKey}", review.Id, pullRequestId, repositoryKey);
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, pullRequestId, repositoryKey));
        return review;
    }

    /// <summary>
    /// Re-runs a finished review from <paramref name="step"/> onwards in its existing worktree: deletes the step runs
    /// from that step on (and the issues, when the last step is among them), clears the review's error and completion
    /// time, and queues it again. Throws <see cref="InvalidOperationException"/> when the review does not exist or is
    /// still queued, preparing its worktree or running.
    /// </summary>
    public async Task RerunFromStepAsync(int reviewId, int step, CancellationToken cancellationToken = default)
    {
        Review review;
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            review = await db.Reviews
                .Include(r => r.StepRuns)
                .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken)
                ?? throw new InvalidOperationException($"Review {reviewId} does not exist.");

            if (review.State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running)
            {
                throw new InvalidOperationException($"Review {reviewId} is {Describe(review.State)}; wait for it to finish.");
            }

            var lastStep = await db.FlowSteps.MaxAsync(s => (int?)s.Step, cancellationToken);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.ReviewStepRuns.Where(s => s.ReviewId == reviewId && s.Step >= step).ExecuteDeleteAsync(cancellationToken);
            if (lastStep is not null && lastStep >= step)
            {
                await db.Issues.Where(i => i.ReviewId == reviewId).ExecuteDeleteAsync(cancellationToken);
            }

            review.Error = null;
            review.CompletedAt = null;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _startGate.Release();
        }

        _queue.Enqueue(review.Id);
        _logger.LogInformation("Re-queued review {ReviewId} to run again from step {Step}", review.Id, step);
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
    }

    public void Dispose() => _startGate.Dispose();

    private static string Describe(ReviewState state) => state switch
    {
        ReviewState.PreparingWorktree => "preparing its worktree",
        _ => state.ToString().ToLowerInvariant(),
    };
}
