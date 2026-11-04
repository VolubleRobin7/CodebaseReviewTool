namespace CodebaseReviewTool.Core.Data.Enums;

/// <summary>Severity of an <see cref="Entities.Issue"/>, as the structured JSON output names it. Stored as text.</summary>
public enum IssueSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Info,
}
