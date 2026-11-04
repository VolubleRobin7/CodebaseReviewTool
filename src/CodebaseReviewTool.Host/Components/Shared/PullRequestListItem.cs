using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Host.Components.Shared;

/// <summary>One open pull request on the home page, with its latest review (if any) and that review's issue counts.</summary>
/// <param name="PullRequest">The cached pull request.</param>
/// <param name="RepositoryName">The repository's display name.</param>
/// <param name="LatestReview">The pull request's newest review, with its step runs loaded; null when never reviewed.</param>
/// <param name="IssueCount">Issues found by <paramref name="LatestReview"/>.</param>
/// <param name="HighCount">Of those, the Critical and High ones.</param>
public sealed record PullRequestListItem(
    PullRequest PullRequest,
    string RepositoryName,
    Review? LatestReview,
    int IssueCount,
    int HighCount)
{
    /// <summary>Unique across repositories; used as the row's render key.</summary>
    public string Key => KeyFor(PullRequest.RepositoryKey, PullRequest.Id);

    /// <summary>The latest review's state, or null when the pull request has never been reviewed.</summary>
    public ReviewState? State { get; } = LatestReview?.State;

    public bool IsReviewing => State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running;

    public static string KeyFor(string repositoryKey, int pullRequestId) => $"{repositoryKey}/{pullRequestId}";
}
