using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>
/// Takes queued reviews off <see cref="ReviewQueue"/>, prepares each one's worktree and runs its flow steps through
/// <see cref="ReviewOrchestrator"/>, with at most <c>Reviews:MaxConcurrent</c> in flight. A review re-queued to re-run
/// from a step reuses its worktree, recreating it when the folder has gone. On start it fails reviews a previous run
/// left part-way and re-queues reviews that were still waiting. A failing review records its error and never stops
/// the service; stopping the service stops the running agent and leaves the review for the next start to mark.
/// </summary>
public sealed class ReviewQueueService(
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    ReviewQueue queue,
    ReviewEvents events,
    IRepositoryRegistry registry,
    WorktreeService worktreeService,
    ReviewOrchestrator orchestrator,
    IOptions<ReviewOptions> options,
    ILogger<ReviewQueueService> logger) : BackgroundService
{
    internal const string InterruptedError = "Interrupted: the tool stopped while this review was running.";

    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly ReviewQueue _queue = queue;
    private readonly ReviewEvents _events = events;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly WorktreeService _worktreeService = worktreeService;
    private readonly ReviewOrchestrator _orchestrator = orchestrator;
    private readonly ReviewOptions _options = options.Value;
    private readonly ILogger<ReviewQueueService> _logger = logger;

    /// <summary>
    /// Recovers the previous run's reviews before the service (and, by registration order, Kestrel) starts, so a
    /// review queued from the UI is never also re-queued here.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!EF.IsDesignTime)
        {
            await RecoverAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (EF.IsDesignTime)
        {
            return;
        }

        using var slots = new SemaphoreSlim(_options.MaxConcurrent, _options.MaxConcurrent);
        var inFlight = new List<Task>();

        try
        {
            await foreach (var reviewId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                inFlight.RemoveAll(t => t.IsCompleted);
                inFlight.Add(Task.Run(
                    async () =>
                    {
                        try
                        {
                            await ProcessAsync(reviewId, stoppingToken);
                        }
                        finally
                        {
                            slots.Release();
                        }
                    },
                    CancellationToken.None));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping; the reviews in flight see the same token.
        }

        // The semaphore is disposed on return, so wait for every review that may still release it.
        await Task.WhenAll(inFlight);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var unfinished = await db.Reviews
            .Include(r => r.StepRuns)
            .Where(r => r.CompletedAt == null && r.Error == null)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var interrupted = unfinished.Where(r => r.StartedAt is not null).ToList();
        foreach (var review in interrupted)
        {
            review.Error = InterruptedError;
            review.CompletedAt = DateTime.UtcNow;

            // The step that was running when the agent was stopped would otherwise read as running forever.
            foreach (var run in review.StepRuns.Where(s => s.CompletedAt is null && s.Error is null))
            {
                run.Error = InterruptedError;
                run.CompletedAt = review.CompletedAt;
            }

            // A worktree added just before the stop is not recorded yet; record it so it is listed and cleaned up.
            var expectedPath = _worktreeService.GetWorktreePath(review.RepositoryKey, review.PullRequestId, review.Id);
            if (string.IsNullOrEmpty(review.WorktreePath) && Directory.Exists(expectedPath))
            {
                review.WorktreePath = expectedPath;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var review in interrupted)
        {
            _logger.LogWarning("Review {ReviewId} was running when the tool stopped; marked failed", review.Id);
            Publish(review);
        }

        foreach (var review in unfinished.Where(r => r.StartedAt is null))
        {
            _queue.Enqueue(review.Id);
            _logger.LogInformation("Re-queued review {ReviewId} of pull request #{PullRequestId}", review.Id, review.PullRequestId);
        }
    }

    private async Task ProcessAsync(int reviewId, CancellationToken cancellationToken)
    {
        try
        {
            if (await PrepareAsync(reviewId, cancellationToken))
            {
                await _orchestrator.RunAsync(reviewId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Left started and unfinished; the next start marks it interrupted.
        }
        catch (Exception ex)
        {
            var error = ex is WorktreeException or InvalidOperationException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning("Review {ReviewId} failed: {Error}", reviewId, error);
            await TryFailAsync(reviewId, error);
        }
    }

    /// <summary>
    /// Makes sure the review has a worktree on disk: creates it for a new review, reuses it for a review re-queued to
    /// re-run from a step, and recreates it when that review's folder has gone. Returns false when the review should
    /// not run (deleted, or already finished).
    /// </summary>
    private async Task<bool> PrepareAsync(int reviewId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.Reviews.FindAsync([reviewId], cancellationToken);
        if (review is null)
        {
            _logger.LogInformation("Review {ReviewId} no longer exists; skipped", reviewId);
            return false;
        }

        if (review.CompletedAt is not null || review.Error is not null)
        {
            _logger.LogInformation("Review {ReviewId} has already finished; skipped", reviewId);
            return false;
        }

        if (review.StartedAt is not null)
        {
            if (!string.IsNullOrEmpty(review.WorktreePath) && Directory.Exists(review.WorktreePath))
            {
                _logger.LogInformation("Review {ReviewId} continues in its existing worktree at {Path}", review.Id, review.WorktreePath);
                return true;
            }

            // Cleared first so the review reads as preparing its worktree until the new one is ready.
            _logger.LogInformation("The worktree of review {ReviewId} has gone; recreating it", review.Id);
            review.WorktreePath = string.Empty;
            review.WorktreeRemovedAt = null;
        }
        else
        {
            review.StartedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        Publish(review);

        var repository = _registry.Find(review.RepositoryKey)
            ?? throw new WorktreeException($"The repository '{review.RepositoryKey}' is no longer configured.");

        var pullRequest = await db.PullRequests.FindAsync([review.PullRequestId, review.RepositoryKey], cancellationToken)
            ?? throw new WorktreeException($"Pull request #{review.PullRequestId} is no longer in the cache.");

        var worktree = await _worktreeService.CreateAsync(
            repository, review.PullRequestId, review.Id, review.SourceCommit, pullRequest.TargetBranch, cancellationToken);

        review.WorktreePath = worktree.Path;
        review.MergeBaseCommit = worktree.MergeBaseCommit;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Worktree ready for review {ReviewId} of pull request #{PullRequestId} at {Path} (merge base {MergeBase})",
            review.Id, review.PullRequestId, worktree.Path, worktree.MergeBaseCommit);
        Publish(review);
        return true;
    }

    /// <summary>Records the error on a fresh context, since the one that failed may hold unsaved or broken state.</summary>
    private async Task TryFailAsync(int reviewId, string error)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            var review = await db.Reviews
                .Include(r => r.StepRuns)
                .FirstOrDefaultAsync(r => r.Id == reviewId, CancellationToken.None);
            if (review is null)
            {
                return;
            }

            review.Error = error;
            review.CompletedAt = DateTime.UtcNow;

            foreach (var run in review.StepRuns.Where(s => s.CompletedAt is null && s.Error is null))
            {
                run.Error = error;
                run.CompletedAt = review.CompletedAt;
            }
            await db.SaveChangesAsync(CancellationToken.None);
            Publish(review);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording the failure of review {ReviewId} failed", reviewId);
        }
    }

    private void Publish(Review review) =>
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
}
