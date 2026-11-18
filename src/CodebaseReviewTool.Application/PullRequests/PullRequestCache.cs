using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Reviews;
using CodebaseReviewTool.Domain.Entities;
using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Application.PullRequests;

/// <summary>What one merge changed.</summary>
/// <param name="Added">Pull requests seen for the first time.</param>
/// <param name="Updated">Cached pull requests whose metadata changed or that reopened.</param>
/// <param name="Closed">Cached open pull requests that are no longer active.</param>
public sealed record PullRequestMergeResult(int Added, int Updated, int Closed)
{
    public bool HasChanges => Added + Updated + Closed > 0;
}

/// <summary>
/// Keeps the <c>PullRequests</c> table in step with the repository host. Rows are never deleted: a pull request that stops being
/// active is marked closed so its reviews keep their pull request.
/// </summary>
public sealed class PullRequestCache(IReviewDbContextFactory dbContextFactory, ReviewEvents events)
{
    private readonly IReviewDbContextFactory _dbContextFactory = dbContextFactory;
    private readonly ReviewEvents _events = events;

    /// <summary>
    /// Upserts <paramref name="active"/> (the complete list of active pull requests in <paramref name="repository"/>)
    /// and closes every cached open pull request of that repository missing from it. Raises
    /// <see cref="ReviewEvents.PullRequestsChanged"/> when anything other than the last-seen time changed.
    /// </summary>
    public async Task<PullRequestMergeResult> MergeAsync(
        ResolvedRepository repository,
        IReadOnlyList<RemotePullRequest> active,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(active);

        await using var db = _dbContextFactory.CreateDbContext();

        var cached = db.GetEntities<PullRequest>()
            .Where(p => p.RepositoryKey == repository.Key)
            .ToDictionary(p => p.Id);

        var now = DateTime.UtcNow;
        var activeIds = new HashSet<int>();
        var added = 0;
        var updated = 0;
        foreach (var source in active)
        {
            activeIds.Add(source.Id);
            if (cached.TryGetValue(source.Id, out var row))
            {
                if (Apply(row, source, isOpen: true, now))
                {
                    updated++;
                }
            }
            else
            {
                row = new PullRequest { Id = source.Id, RepositoryKey = repository.Key };
                Apply(row, source, isOpen: true, now);
                db.Add(row);
                added++;
            }
        }

        var closed = 0;
        foreach (var row in cached.Values.Where(r => r.IsOpen && !activeIds.Contains(r.Id)))
        {
            row.IsOpen = false;
            closed++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = new PullRequestMergeResult(added, updated, closed);
        if (result.HasChanges)
        {
            _events.PublishPullRequestsChanged();
        }

        return result;
    }

    /// <summary>
    /// Upserts one pull request fetched on demand (for example by the API, which needs the current head commit rather
    /// than the one from the last poll) and returns the saved row. Unlike <see cref="MergeAsync"/> it closes nothing
    /// else; the row is open only while the host reports it active. Raises
    /// <see cref="ReviewEvents.PullRequestsChanged"/> when anything other than the last-seen time changed.
    /// </summary>
    public async Task<PullRequest> UpsertAsync(ResolvedRepository repository, RemotePullRequest source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(source);

        await using var db = _dbContextFactory.CreateDbContext();

        var row = db.GetEntities<PullRequest>().FirstOrDefault(p => p.Id == source.Id && p.RepositoryKey == repository.Key);
        bool changed;
        if (row is null)
        {
            row = new PullRequest { Id = source.Id, RepositoryKey = repository.Key };
            Apply(row, source, source.IsActive, DateTime.UtcNow);
            db.Add(row);
            changed = true;
        }
        else
        {
            changed = Apply(row, source, source.IsActive, DateTime.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (changed)
        {
            _events.PublishPullRequestsChanged();
        }

        return row;
    }

    /// <summary>
    /// Copies <paramref name="source"/> onto <paramref name="row"/> and returns true when anything other than the
    /// last-seen time changed. <see cref="PullRequest.LastSeenAt"/> moves on every poll, so on its own it is not a
    /// change worth refreshing the UI for.
    /// </summary>
    private static bool Apply(PullRequest row, RemotePullRequest source, bool isOpen, DateTime now)
    {
        var before = Fields.Of(row);

        row.Title = source.Title;
        row.Description = source.Description ?? string.Empty;
        row.AuthorName = source.AuthorName;
        row.SourceBranch = source.SourceBranch;
        row.TargetBranch = source.TargetBranch;
        // Without a merge yet there is no commit; keep the last one known rather than forget it.
        row.SourceCommit = source.SourceCommit ?? row.SourceCommit;
        row.Status = source.Status;
        row.IsDraft = source.IsDraft;
        row.MergeStatus = source.MergeStatus;
        row.CreatedAt = source.CreatedAt;
        row.Url = source.WebUrl;
        row.IsOpen = isOpen;
        row.LastSeenAt = now;

        return Fields.Of(row) != before;
    }

    /// <summary>Every column of a cached pull request except its key and <see cref="PullRequest.LastSeenAt"/>.</summary>
    private readonly record struct Fields(
        string Title,
        string Description,
        string AuthorName,
        string SourceBranch,
        string TargetBranch,
        string SourceCommit,
        string Status,
        bool IsDraft,
        string MergeStatus,
        DateTime CreatedAt,
        string Url,
        bool IsOpen)
    {
        public static Fields Of(PullRequest row) => new(
            row.Title,
            row.Description,
            row.AuthorName,
            row.SourceBranch,
            row.TargetBranch,
            row.SourceCommit,
            row.Status,
            row.IsDraft,
            row.MergeStatus,
            row.CreatedAt,
            row.Url,
            row.IsOpen);
    }
}
