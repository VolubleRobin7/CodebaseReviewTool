using CodebaseReviewTool.Core.Configuration;

namespace CodebaseReviewTool.Host.Components.Shared;

/// <summary>Describes when finished reviews have their worktrees removed, from <see cref="GitOptions"/>.</summary>
public static class WorktreeCleanupText
{
    /// <summary>
    /// "14 days after a review finishes, or 1 day after its pull request is completed or abandoned", to follow
    /// "removed automatically".
    /// </summary>
    public static string Describe(GitOptions options) =>
        $"{After(options.AutoCleanupAfterDays)} a review finishes, or {After(options.AutoCleanupAfterPullRequestClosedDays)} its pull request is completed or abandoned";

    public static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    // 0 days means the next cleanup run, which is up to six hours away.
    private static string After(int days) => days == 0 ? "soon after" : $"{Days(days)} after";
}
