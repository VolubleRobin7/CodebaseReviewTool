namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>A pull request as a repository host reports it, in the host-neutral shape the cache stores.</summary>
/// <param name="Id">The host's pull request id.</param>
/// <param name="Title">The title.</param>
/// <param name="Description">The description; null when there is none.</param>
/// <param name="AuthorName">Who created it; empty when the host does not say.</param>
/// <param name="SourceBranch">The source branch, without <c>refs/heads/</c>.</param>
/// <param name="TargetBranch">The target branch, without <c>refs/heads/</c>.</param>
/// <param name="SourceCommit">The head commit of the source branch; null until the host has computed it.</param>
/// <param name="Status">The host's status text, such as <c>active</c>, <c>completed</c> or <c>abandoned</c>.</param>
/// <param name="IsActive">True while the pull request is open (not completed or abandoned).</param>
/// <param name="IsDraft">True while it is a draft, not yet ready for review.</param>
/// <param name="MergeStatus">The host's merge status text, such as <c>succeeded</c> or <c>conflicts</c>.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="WebUrl">The page a person opens to see it.</param>
public sealed record RemotePullRequest(
    int Id,
    string Title,
    string? Description,
    string AuthorName,
    string SourceBranch,
    string TargetBranch,
    string? SourceCommit,
    string Status,
    bool IsActive,
    bool IsDraft,
    string MergeStatus,
    DateTime CreatedAt,
    string WebUrl);
