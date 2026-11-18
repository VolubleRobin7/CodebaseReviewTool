using CodebaseReviewTool.Application.Common.Extensions;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Domain.Entities;
using CodebaseReviewTool.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Application.Reviews;

/// <summary>Starts, re-runs and cancels reviews, and announces each change.</summary>
public sealed class ReviewService(
    IReviewDbContextFactory dbContextFactory,
    ReviewQueue queue,
    ReviewRuns runs,
    ReviewEvents events,
    IRepositoryRegistry registry,
    ISourceControl sourceControl,
    ILogger<ReviewService> logger) : IDisposable
{
    // Stopping the agent kills its process tree, which takes moments; this only guards against a hang.
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(30);

    private readonly IReviewDbContextFactory _dbContextFactory = dbContextFactory;
    private readonly ReviewQueue _queue = queue;
    private readonly ReviewRuns _runs = runs;
    private readonly ReviewEvents _events = events;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly ISourceControl _sourceControl = sourceControl;
    private readonly ILogger<ReviewService> _logger = logger;

    // Serialises the "already reviewing?" check with the insert, so a double click cannot queue two reviews.
    private readonly SemaphoreSlim _startGate = new(1, 1);

    /// <summary>
    /// Creates a <see cref="ReviewState.Queued"/> review of the cached pull request, queues it and raises
    /// <see cref="ReviewEvents.ReviewChanged"/>. Throws <see cref="InvalidOperationException"/> when the pull request is
    /// not in the cache, and its subclass <see cref="ReviewAlreadyActiveException"/> when the pull request already has
    /// a review that is queued, preparing its worktree or running.
    /// </summary>
    public async Task<Review> StartReviewAsync(int pullRequestId, string repositoryKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryKey);

        Review review;
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = _dbContextFactory.CreateDbContext();

            var pullRequest = db.GetEntities<PullRequest>().FirstOrDefault(p => p.Id == pullRequestId && p.RepositoryKey == repositoryKey)
                ?? throw new InvalidOperationException(
                    $"Pull request #{pullRequestId} in '{repositoryKey}' is not in the cache; wait for the next poll.");

            // State is derived, so load the reviews that could still be in flight (no error, not completed) with
            // their step runs and ask each one.
            var unfinished = db.GetEntities<Review>()
                .Where(r => r.PullRequestId == pullRequestId && r.RepositoryKey == repositoryKey && r.Error == null && r.CompletedAt == null)
                .ToListWithStepRuns();

            if (unfinished.FirstOrDefault(r => r.State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running) is { } active)
            {
                throw new ReviewAlreadyActiveException(
                    active.Id,
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
            db.Add(review);
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
            await using var db = _dbContextFactory.CreateDbContext();

            review = db.GetEntities<Review>()
                .Where(r => r.Id == reviewId)
                .FirstOrDefaultWithStepRuns()
                ?? throw new InvalidOperationException($"Review {reviewId} does not exist.");

            if (review.State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running)
            {
                throw new InvalidOperationException($"Review {reviewId} is {Describe(review.State)}; wait for it to finish.");
            }

            var lastStep = db.GetEntities<FlowStep>().Max(s => (int?)s.Step);

            // Removed and reset in one save, so a failure part-way leaves the review as it was.
            foreach (var run in review.StepRuns.Where(s => s.Step >= step).ToList())
            {
                db.Remove(run);
            }

            if (lastStep is not null && lastStep >= step)
            {
                foreach (var issue in db.GetEntities<Issue>().Where(i => i.ReviewId == reviewId).ToList())
                {
                    db.Remove(issue);
                }
            }

            review.Error = null;
            review.CompletedAt = null;
            review.CancelledAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _startGate.Release();
        }

        _queue.Enqueue(review.Id);
        _logger.LogInformation("Re-queued review {ReviewId} to run again from step {Step}", review.Id, step);
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
    }

    /// <summary>
    /// Stops a review that is queued, preparing its worktree or running, and waits for its agent to stop. When at least
    /// one step had succeeded, the review is kept as <see cref="ReviewState.Cancelled"/> with the step that was running
    /// marked cancelled, so its report shows what finished. Otherwise there is nothing to report: the review and its
    /// worktree are removed, as if it had never been started. Throws <see cref="InvalidOperationException"/> when the
    /// review does not exist, is not in flight, or does not stop in time.
    /// </summary>
    public async Task<ReviewCancelOutcome> CancelReviewAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            ReviewState state;
            await using (var db = _dbContextFactory.CreateDbContext())
            {
                var current = db.GetEntities<Review>()
                    .Where(r => r.Id == reviewId)
                    .FirstOrDefaultWithStepRuns()
                    ?? throw new InvalidOperationException($"Review {reviewId} does not exist.");
                state = current.State;
            }

            if (state is not (ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running))
            {
                throw new InvalidOperationException($"Review {reviewId} is {Describe(state)}, so there is nothing to stop.");
            }

            // Holding the start gate keeps a re-run of this review from being queued while it stops.
            try
            {
                await _runs.CancelAsync(reviewId, isQueued: state == ReviewState.Queued).WaitAsync(_stopTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"Review {reviewId} is still stopping; try again in a moment.");
            }

            return await RecordCancellationAsync(reviewId, cancellationToken);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public void Dispose() => _startGate.Dispose();

    /// <summary>Records a review whose run has stopped (or never started) as cancelled, or removes it when nothing finished.</summary>
    private async Task<ReviewCancelOutcome> RecordCancellationAsync(int reviewId, CancellationToken cancellationToken)
    {
        await using var db = _dbContextFactory.CreateDbContext();

        // Read again: the run may have finished a step, or the whole review, before it saw the cancellation.
        var review = db.GetEntities<Review>()
            .Where(r => r.Id == reviewId)
            .FirstOrDefaultWithStepRuns();
        if (review is null)
        {
            return ReviewCancelOutcome.Discarded;
        }

        if (review.State is ReviewState.Completed or ReviewState.Failed or ReviewState.Cancelled)
        {
            _logger.LogInformation("Review {ReviewId} finished before it could be cancelled", reviewId);
            return ReviewCancelOutcome.AlreadyFinished;
        }

        var outcome = ReviewCancelOutcome.Cancelled;
        if (!review.StepRuns.Any(s => s.State == StepRunState.Succeeded) && await TryRemoveWorktreeAsync(review, cancellationToken))
        {
            db.Remove(review);
            outcome = ReviewCancelOutcome.Discarded;
            _logger.LogInformation("Review {ReviewId} was cancelled before any step finished; removed it", reviewId);
        }
        else
        {
            // A review with nothing finished is still kept when its worktree could not be removed, so the worktree stays
            // listed and the cleanup service can remove it later.
            var now = DateTime.UtcNow;
            review.CancelledAt = now;
            review.CompletedAt = now;
            foreach (var run in review.StepRuns.Where(s => s.State is StepRunState.Running or StepRunState.Pending))
            {
                run.CancelledAt = now;
                run.CompletedAt = now;
            }

            _logger.LogInformation("Review {ReviewId} was cancelled", reviewId);
        }

        await db.SaveChangesAsync(cancellationToken);
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
        return outcome;
    }

    /// <summary>
    /// Removes the worktree of a review that is about to be deleted; returns false when it could not be removed. The
    /// path may not be recorded yet when the review was stopped while git was adding the worktree.
    /// </summary>
    private async Task<bool> TryRemoveWorktreeAsync(Review review, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrEmpty(review.WorktreePath)
            ? _sourceControl.GetWorktreePath(review.RepositoryKey, review.PullRequestId, review.Id)
            : review.WorktreePath;

        if (string.IsNullOrEmpty(review.WorktreePath) && !Directory.Exists(path))
        {
            return true;
        }

        if (_registry.Find(review.RepositoryKey) is not { } repository)
        {
            _logger.LogWarning("Not removing the worktree of review {ReviewId}: repository {RepositoryKey} is no longer configured", review.Id, review.RepositoryKey);
            review.WorktreePath = path;
            return false;
        }

        var result = await _sourceControl.RemoveWorktreeAsync(repository, path, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Could not remove the worktree of cancelled review {ReviewId} at {Path}: {Error}", review.Id, path, result.Detail);
            review.WorktreePath = path;
            return false;
        }

        return true;
    }

    private static string Describe(ReviewState state) => state switch
    {
        ReviewState.PreparingWorktree => "preparing its worktree",
        _ => state.ToString().ToLowerInvariant(),
    };
}

/// <summary>What <see cref="ReviewService.CancelReviewAsync"/> did.</summary>
public enum ReviewCancelOutcome
{
    /// <summary>The review was stopped and kept, with the steps that finished.</summary>
    Cancelled,

    /// <summary>No step had finished, so the review and its worktree were removed.</summary>
    Discarded,

    /// <summary>The review finished before the cancellation reached it; nothing changed.</summary>
    AlreadyFinished,
}
