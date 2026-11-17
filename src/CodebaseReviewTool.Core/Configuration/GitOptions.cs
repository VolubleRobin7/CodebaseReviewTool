using System.ComponentModel.DataAnnotations;

namespace CodebaseReviewTool.Core.Configuration;

public sealed class GitOptions
{
    public const string SectionName = "Git";

    /// <summary>git executable; a bare name is resolved through PATH.</summary>
    [Required(ErrorMessage = "Git:ExecutablePath is required.")]
    [ConfirmNotPlaceholder]
    public string ExecutablePath { get; set; } = "git";

    /// <summary>Folder under which review worktrees are created. Environment variables are expanded at startup.</summary>
    [Required(ErrorMessage = "Git:WorktreeRoot is required.")]
    [ConfirmNotPlaceholder]
    public string WorktreeRoot { get; set; } = string.Empty;

    /// <summary>
    /// Days a finished review keeps its worktree while its pull request is still in progress (open). Reviews still
    /// queued or running are never cleaned up.
    /// </summary>
    [Range(0, 3650, ErrorMessage = "Git:AutoCleanupAfterDays must be between {1} and {2}.")]
    public int AutoCleanupAfterDays { get; set; } = 14;

    /// <summary>
    /// Days a finished review keeps its worktree once its pull request has been completed or abandoned. 0 removes it
    /// on the next cleanup run, which happens every six hours.
    /// </summary>
    [Range(0, 3650, ErrorMessage = "Git:AutoCleanupAfterPullRequestClosedDays must be between {1} and {2}.")]
    public int AutoCleanupAfterPullRequestClosedDays { get; set; } = 1;

    public bool FetchBeforeReview { get; set; } = true;

    /// <summary>Timeout for each git command a review runs (fetch, worktree add and remove, merge-base, diff).</summary>
    [Range(1, 1440, ErrorMessage = "Git:CommandTimeoutMinutes must be between {1} and {2}.")]
    public int CommandTimeoutMinutes { get; set; } = 10;
}
