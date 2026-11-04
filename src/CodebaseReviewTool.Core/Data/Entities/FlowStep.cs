namespace CodebaseReviewTool.Core.Data.Entities;

/// <summary>One step of the global review flow. Steps run in ascending <see cref="Step"/> order.</summary>
public class FlowStep
{
    /// <summary>Primary key and sequence number, starting at 0.</summary>
    public int Step { get; set; }

    /// <summary>Prompt text with placeholders; a leading <c>/skill-name</c> invokes an agent skill. Seeded from the shipped default.</summary>
    public string PromptText { get; set; } = string.Empty;

    /// <summary>Model passed to the agent for this step; null uses the configured default.</summary>
    public string? Model { get; set; }

    /// <summary>Timeout for this step; null uses the configured default.</summary>
    public int? TimeoutMinutes { get; set; }

    /// <summary>UTC.</summary>
    public DateTime UpdatedAt { get; set; }
}
