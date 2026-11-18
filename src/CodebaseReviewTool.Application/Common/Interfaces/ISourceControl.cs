using CodebaseReviewTool.Application.Common.Exceptions;
using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>
/// The configured local clones and the worktree each review runs in. Worktrees are detached at the pull request's
/// source commit, so a branch checked out elsewhere never blocks one.
/// </summary>
public interface ISourceControl
{
    /// <summary>The clones in configuration, in configured order.</summary>
    IReadOnlyList<ConfiguredClone> ConfiguredClones { get; }

    /// <summary>
    /// Chooses the remote a clone is reviewed through (the configured one, else <c>origin</c>, else the only remote)
    /// and reads its URL. Fails, with a message naming the clone, when the folder is not a repository or no remote
    /// can be chosen.
    /// </summary>
    Task<ServiceResult<CloneRemote>> ReadRemoteAsync(ConfiguredClone clone, CancellationToken cancellationToken);

    /// <summary>The directory a review's worktree is created in.</summary>
    string GetWorktreePath(string repositoryKey, int pullRequestId, int reviewId);

    /// <summary>
    /// Fetches when configured to, adds a detached worktree at <paramref name="sourceCommit"/> and works out the merge
    /// base with the remote-tracking <paramref name="targetBranch"/>. Throws <see cref="WorktreeException"/> with a
    /// readable message when any of that fails; a worktree added before the failure is removed again.
    /// </summary>
    Task<WorktreeInfo> CreateWorktreeAsync(
        ResolvedRepository repository,
        int pullRequestId,
        int reviewId,
        string sourceCommit,
        string targetBranch,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the files changed between <paramref name="mergeBase"/> and <paramref name="sourceCommit"/>. Throws
    /// <see cref="WorktreeException"/> when they cannot be listed.
    /// </summary>
    Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(string worktreePath, string mergeBase, string sourceCommit, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a review's worktree. A path that is already gone counts as removed. Expected failures are returned, not
    /// thrown.
    /// </summary>
    Task<WorktreeRemoveResult> RemoveWorktreeAsync(ResolvedRepository repository, string worktreePath, CancellationToken cancellationToken);
}
