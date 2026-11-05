using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Git;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Core.Reporting;

/// <summary>Reads one review and everything its report shows from the database.</summary>
public sealed class ReportDataLoader(IDbContextFactory<ReviewDbContext> dbContextFactory, IRepositoryRegistry registry)
{
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly IRepositoryRegistry _registry = registry;

    /// <summary>Returns the review's report, or null when there is no review with that id.</summary>
    public async Task<ReportData?> LoadAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.Reviews
            .AsNoTracking()
            .Include(r => r.PullRequest)
            .Include(r => r.StepRuns)
            .Include(r => r.Issues)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);

        if (review?.PullRequest is null)
        {
            return null;
        }

        // Each review's state is derived from its step runs, so they come along; issues are only counted.
        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(r => r.StepRuns)
            .Where(r => r.PullRequestId == review.PullRequestId && r.RepositoryKey == review.RepositoryKey)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var ids = reviews.Select(r => r.Id).ToList();
        var issueCounts = await db.Issues
            .Where(i => ids.Contains(i.ReviewId))
            .GroupBy(i => i.ReviewId)
            .Select(g => new { ReviewId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(c => c.ReviewId, c => c.Count, cancellationToken);

        var history = reviews
            .Select(r => new ReviewHistoryEntry(r.Id, r.QueuedAt, r.CompletedAt, r.State, issueCounts.GetValueOrDefault(r.Id)))
            .ToList();

        var flowSteps = await db.FlowSteps.AsNoTracking().OrderBy(s => s.Step).ToListAsync(cancellationToken);
        var repositoryName = _registry.Find(review.RepositoryKey)?.DisplayName ?? review.RepositoryKey;

        return new ReportData(review, review.PullRequest, repositoryName, flowSteps, history);
    }
}
