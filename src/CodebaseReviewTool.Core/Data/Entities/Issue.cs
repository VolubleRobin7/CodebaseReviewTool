using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Data.Entities;

/// <summary>One issue parsed from a review's structured JSON output.</summary>
public class Issue
{
    public int Id { get; set; }

    public int ReviewId { get; set; }

    public Review? Review { get; set; }

    public IssueSeverity Severity { get; set; }

    public string Category { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public int? Line { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Suggestion { get; set; }

    /// <summary>When the issue was posted to its pull request as a comment thread (UTC); null until then.</summary>
    public DateTime? PostedAt { get; set; }

    /// <summary>The id of the DevOps comment thread the issue was posted as; null until it is posted.</summary>
    public int? PostedThreadId { get; set; }
}
