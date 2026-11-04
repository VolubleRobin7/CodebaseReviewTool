using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>
/// Removes the worktrees of reviews that finished (completed or failed) more than <c>Git:AutoCleanupAfterDays</c> days
/// ago: shortly after startup, then every six hours. A review still in progress is never touched. A worktree that
/// cannot be removed is logged and tried again on the next run (or removed from the Worktrees page).
/// </summary>
public sealed class WorktreeCleanupService(
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    IRepositoryRegistry registry,
    WorktreeService worktreeService,
    ReviewEvents events,
    IOptions<GitOptions> options,
    ILogger<WorktreeCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _interval = TimeSpan.FromHours(6);

    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly WorktreeService _worktreeService = worktreeService;
    private readonly ReviewEvents _events = events;
    private readonly GitOptions _options = options.Value;
    private readonly ILogger<WorktreeCleanupService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (EF.IsDesignTime)
        {
            return;
        }

        try
        {
            await Task.Delay(_startupDelay, stoppingToken);
            while (true)
            {
                await CleanUpAsync(stoppingToken);
                await Task.Delay(_interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }

    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_options.AutoCleanupAfterDays);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var expired = await db.Reviews
                .Where(r => r.WorktreePath != "" && r.WorktreeRemovedAt == null && r.CompletedAt != null && r.CompletedAt < cutoff)
                .OrderBy(r => r.Id)
                .ToListAsync(cancellationToken);

            foreach (var review in expired)
            {
                var repository = _registry.Find(review.RepositoryKey);
                if (repository is null)
                {
                    _logger.LogWarning(
                        "Not removing the worktree of review {ReviewId}: repository {RepositoryKey} is no longer configured",
                        review.Id, review.RepositoryKey);
                    continue;
                }

                var result = await _worktreeService.RemoveAsync(repository, review.WorktreePath, cancellationToken);
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Could not remove the worktree of review {ReviewId} at {Path}: {Error}", review.Id, review.WorktreePath, result.Detail);
                    continue;
                }

                review.WorktreeRemovedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Removed the worktree of review {ReviewId} at {Path}: {Detail}", review.Id, review.WorktreePath, result.Detail);
                _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Worktree cleanup failed; it runs again in {Interval}", _interval);
        }
    }
}
