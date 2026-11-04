using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>What one merge changed.</summary>
/// <param name="Added">Pull requests seen for the first time.</param>
/// <param name="Updated">Cached pull requests whose metadata changed or that reopened.</param>
/// <param name="Closed">Cached open pull requests that are no longer active.</param>
public sealed record PullRequestMergeResult(int Added, int Updated, int Closed)
{
    public bool HasChanges => Added + Updated + Closed > 0;
}

/// <summary>
/// Keeps the <c>PullRequests</c> table in step with DevOps. Rows are never deleted: a pull request that stops being
/// active is marked closed so its reviews keep their pull request.
/// </summary>
public sealed class PullRequestCache(IDbContextFactory<ReviewDbContext> dbContextFactory, ReviewEvents events)
{
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly ReviewEvents _events = events;

    /// <summary>
    /// Upserts <paramref name="active"/> (the complete list of active pull requests in <paramref name="repository"/>)
    /// and closes every cached open pull request of that repository missing from it. Raises
    /// <see cref="ReviewEvents.PullRequestsChanged"/> when anything other than the last-seen time changed.
    /// </summary>
    public async Task<PullRequestMergeResult> MergeAsync(
        ResolvedRepository repository,
        IReadOnlyList<DevOpsPullRequest> active,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(active);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cached = await db.PullRequests
            .Where(p => p.RepositoryKey == repository.Key)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var activeIds = new HashSet<int>();
        foreach (var source in active)
        {
            activeIds.Add(source.PullRequestId);
            if (!cached.TryGetValue(source.PullRequestId, out var row))
            {
                row = new PullRequest { Id = source.PullRequestId, RepositoryKey = repository.Key };
                db.PullRequests.Add(row);
            }

            row.Title = source.Title;
            row.Description = source.Description ?? string.Empty;
            row.AuthorName = source.CreatedBy?.DisplayName ?? string.Empty;
            row.SourceBranch = source.SourceBranch;
            row.TargetBranch = source.TargetBranch;
            // Without a merge yet there is no commit; keep the last one known rather than forget it.
            row.SourceCommit = source.LastMergeSourceCommit?.CommitId ?? row.SourceCommit;
            row.Status = source.Status;
            row.CreatedAt = source.CreationDate;
            row.Url = source.WebUrl;
            row.LastSeenAt = now;
            row.IsOpen = true;
        }

        var closed = 0;
        foreach (var row in cached.Values.Where(r => r.IsOpen && !activeIds.Contains(r.Id)))
        {
            row.IsOpen = false;
            closed++;
        }

        db.ChangeTracker.DetectChanges();
        var entries = db.ChangeTracker.Entries<PullRequest>().ToList();
        var added = entries.Count(e => e.State == EntityState.Added);
        var updated = entries.Count(e => e.State == EntityState.Modified && HasMeaningfulChange(e) && e.Entity.IsOpen);

        await db.SaveChangesAsync(cancellationToken);

        var result = new PullRequestMergeResult(added, updated, closed);
        if (result.HasChanges)
        {
            _events.PublishPullRequestsChanged();
        }

        return result;
    }

    // LastSeenAt moves on every poll; only the other columns count as a change worth refreshing the UI for.
    private static bool HasMeaningfulChange(EntityEntry<PullRequest> entry) =>
        entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(PullRequest.LastSeenAt));
}
