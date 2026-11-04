namespace CodebaseReviewTool.Core.Data.Entities;

/// <summary>One step of the global review flow. Steps run in ascending <see cref="Step"/> order.</summary>
public class FlowStep
{
    /// <summary>Primary key and sequence number, starting at 0.</summary>
    public int Step { get; set; }

    /// <summary>Prompt text; a leading <c>/skill-name</c> runs a skill. Empty until the user edits the flow.</summary>
    public string PromptText { get; set; } = string.Empty;

    /// <summary>Overrides <c>Claude:DefaultModel</c> for this step.</summary>
    public string? Model { get; set; }

    /// <summary>Overrides <c>Claude:TimeoutMinutes</c> for this step.</summary>
    public int? TimeoutMinutes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; set; }
}
