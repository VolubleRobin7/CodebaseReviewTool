using CodebaseReviewTool.Application.Common.Extensions;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Domain.Entities;

namespace CodebaseReviewTool.Application.Reporting;

/// <summary>Reads one review and everything its report shows from the database.</summary>
public sealed class ReportDataLoader(IReviewDbContextFactory dbContextFactory, IRepositoryRegistry registry)
{
    private readonly IReviewDbContextFactory _dbContextFactory = dbContextFactory;
    private readonly IRepositoryRegistry _registry = registry;

    /// <summary>Returns the review's report, or null when there is no review with that id.</summary>
    public async Task<ReportData?> LoadAsync(int reviewId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var db = _dbContextFactory.CreateDbContext();

        // One query per collection, so a review's step runs and its issues are not multiplied together.
        var review = db.GetEntities<Review>().FirstOrDefault(r => r.Id == reviewId);
        var pullRequest = review is null
            ? null
            : db.GetEntities<PullRequest>().FirstOrDefault(p => p.Id == review.PullRequestId && p.RepositoryKey == review.RepositoryKey);
        if (review is null || pullRequest is null)
        {
            return null;
        }

        review.PullRequest = pullRequest;
        review.StepRuns = [.. db.GetEntities<ReviewStepRun>().Where(s => s.ReviewId == reviewId)];
        review.Issues = [.. db.GetEntities<Issue>().Where(i => i.ReviewId == reviewId)];

        // Each review's state is derived from its step runs, so they come along; issues are only counted.
        var reviews = db.GetEntities<Review>()
            .Where(r => r.PullRequestId == review.PullRequestId && r.RepositoryKey == review.RepositoryKey)
            .OrderByDescending(r => r.Id)
            .ToListWithStepRuns();

        var ids = reviews.Select(r => r.Id).ToList();
        var issueCounts = db.GetEntities<Issue>()
            .Where(i => ids.Contains(i.ReviewId))
            .GroupBy(i => i.ReviewId)
            .Select(g => new { ReviewId = g.Key, Count = g.Count() })
            .ToDictionary(c => c.ReviewId, c => c.Count);

        var history = reviews
            .Select(r => new ReviewHistoryEntry(r.Id, r.QueuedAt, r.CompletedAt, r.State, issueCounts.GetValueOrDefault(r.Id)))
            .ToList();

        var flowSteps = db.GetEntities<FlowStep>().OrderBy(s => s.Step).ToList();
        var repositoryName = _registry.Find(review.RepositoryKey)?.DisplayName ?? review.RepositoryKey;

        return new ReportData(review, pullRequest, repositoryName, flowSteps, history);
    }
}
