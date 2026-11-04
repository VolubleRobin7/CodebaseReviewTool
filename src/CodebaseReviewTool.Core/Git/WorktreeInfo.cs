namespace CodebaseReviewTool.Core.Git;

/// <summary>A worktree created for one review.</summary>
/// <param name="Path">Full path of the worktree directory.</param>
/// <param name="MergeBaseCommit">The merge base of the source commit and the remote-tracking target branch.</param>
public sealed record WorktreeInfo(string Path, string MergeBaseCommit);

/// <summary>One line of <c>git diff --name-status</c>.</summary>
/// <param name="Status">The status letters as git prints them, e.g. <c>M</c>, <c>A</c>, <c>D</c>, <c>R100</c>.</param>
/// <param name="Path">The file's path relative to the repository root; for a rename or copy, the new path.</param>
public sealed record ChangedFile(string Status, string Path);

/// <summary>Outcome of removing a worktree.</summary>
/// <param name="Succeeded">True when the directory is gone and git no longer lists the worktree.</param>
/// <param name="Detail">What was done, or the git or IO error text when it failed.</param>
public sealed record WorktreeRemoveResult(bool Succeeded, string Detail)
{
    public static WorktreeRemoveResult Success(string detail) => new(true, detail);

    public static WorktreeRemoveResult Failure(string detail) => new(false, detail);
}

/// <summary>A worktree could not be prepared; the message is written for the user.</summary>
public sealed class WorktreeException(string message) : Exception(message);
