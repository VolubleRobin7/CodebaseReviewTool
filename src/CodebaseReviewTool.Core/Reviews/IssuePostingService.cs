using System.Text;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.DevOps;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>Outcome of <see cref="IssuePostingService.PostAsync"/>.</summary>
/// <param name="Requested">Issues asked for that had not been posted already.</param>
/// <param name="Posted">Issues posted by this call.</param>
/// <param name="Error">Why posting stopped early; null when every requested issue was posted.</param>
public sealed record IssuePostResult(int Requested, int Posted, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Posts a review's issues to its pull request, one active comment thread per issue, anchored to the issue's file and
/// line where it has them. Each posted issue records its thread, so it is never posted twice.
/// </summary>
public sealed class IssuePostingService(
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    IServiceScopeFactory scopeFactory,
    IRepositoryRegistry registry,
    ReviewEvents events,
    ILogger<IssuePostingService> logger) : IDisposable
{
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly ReviewEvents _events = events;
    private readonly ILogger<IssuePostingService> _logger = logger;

    // Serialises posting, so a double click or a second browser tab cannot post the same issue twice.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Posts the issues of review <paramref name="reviewId"/> named by <paramref name="issueIds"/>, in report order,
    /// skipping any already posted. Stops at the first failure, since what fails one (credentials, permissions, the
    /// network) fails the rest; the issues posted before it stay recorded. Raises
    /// <see cref="ReviewEvents.ReviewChanged"/> when anything was posted. Throws <see cref="InvalidOperationException"/>
    /// when the review does not exist or its repository is no longer configured.
    /// </summary>
    public async Task<IssuePostResult> PostAsync(int reviewId, IReadOnlyCollection<int> issueIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issueIds);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var review = await db.Reviews
                .Include(r => r.PullRequest)
                .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken)
                ?? throw new InvalidOperationException($"Review {reviewId} no longer exists.");

            var repository = _registry.Find(review.RepositoryKey)
                ?? throw new InvalidOperationException(
                    $"The repository '{review.RepositoryKey}' is no longer configured, so its pull request cannot be found.");

            var ids = issueIds.ToHashSet();
            var issues = await db.Issues
                .Where(i => i.ReviewId == reviewId && ids.Contains(i.Id) && i.PostedAt == null)
                .ToListAsync(cancellationToken);

            // The same order as the report, so the threads read in that order on the pull request.
            issues = [.. issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Line ?? 0)
                .ThenBy(i => i.Id)];

            if (issues.Count == 0)
            {
                return new IssuePostResult(0, 0, null);
            }

            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IDevOpsClient>();

            var posted = 0;
            string? error = null;
            foreach (var issue in issues)
            {
                var thread = new DevOpsNewThread(CommentText(issue, review.SourceCommit), issue.File, issue.Line);
                var result = await client.CreatePullRequestThreadAsync(repository, review.PullRequestId, thread, cancellationToken);
                if (!result.Succeeded)
                {
                    error = result.Error;
                    _logger.LogWarning("Posting issue {IssueId} of review {ReviewId} to pull request {PullRequestId} failed: {Error}",
                        issue.Id, reviewId, review.PullRequestId, result.Error);
                    break;
                }

                // Saved one at a time, so an issue that reached DevOps is recorded even if a later one fails.
                issue.PostedAt = DateTime.UtcNow;
                issue.PostedThreadId = result.Value!.Id;
                await db.SaveChangesAsync(CancellationToken.None);
                posted++;
            }

            _logger.LogInformation("Posted {Posted} of {Requested} issues of review {ReviewId} to pull request {PullRequestId}",
                posted, issues.Count, reviewId, review.PullRequestId);

            if (posted > 0)
            {
                _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));
            }

            return new IssuePostResult(issues.Count, posted, error);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// The comment an issue is posted as, in markdown: severity and category, the title, the description, the suggested
    /// fix and the location, then a line saying where it came from.
    /// </summary>
    internal static string CommentText(Issue issue, string reviewedCommit)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var text = new StringBuilder();

        var heading = string.IsNullOrWhiteSpace(issue.Category)
            ? $"**{issue.Severity}**"
            : $"**{issue.Severity}** · {issue.Category.Trim()}";
        text.Append(heading).Append("\n\n");

        if (!string.IsNullOrWhiteSpace(issue.Title))
        {
            text.Append("**").Append(issue.Title.Trim()).Append("**\n\n");
        }

        if (!string.IsNullOrWhiteSpace(issue.Description))
        {
            text.Append(issue.Description.Trim()).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(issue.Suggestion))
        {
            text.Append("**Suggested fix:** ").Append(issue.Suggestion.Trim()).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(issue.File))
        {
            text.Append('`').Append(ReportFormat.Location(issue).Replace("`", "'", StringComparison.Ordinal)).Append("`\n\n");
        }

        var commit = string.IsNullOrEmpty(reviewedCommit) ? string.Empty : $" of commit {ReportFormat.ShortCommit(reviewedCommit)}";
        text.Append("_Raised by Codebase Review Tool from its review").Append(commit).Append("._");

        return text.ToString().ReplaceLineEndings("\n");
    }
}
