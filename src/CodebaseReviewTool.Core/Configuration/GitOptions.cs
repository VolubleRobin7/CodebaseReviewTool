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

    [Range(0, 3650, ErrorMessage = "Git:AutoCleanupAfterDays must be between {1} and {2}.")]
    public int AutoCleanupAfterDays { get; set; } = 14;

    public bool FetchBeforeReview { get; set; } = true;
}
