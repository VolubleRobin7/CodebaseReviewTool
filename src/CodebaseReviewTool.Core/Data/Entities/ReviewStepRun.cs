using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Data.Entities;

/// <summary>One agent run inside a review. All times are UTC.</summary>
public class ReviewStepRun
{
    public int Id { get; set; }

    public int ReviewId { get; set; }

    public Review? Review { get; set; }

    /// <summary>Sequence number of the flow step this run executed; matches <see cref="FlowStep.Step"/>.</summary>
    public int Step { get; set; }

    /// <summary>Derived from <see cref="StartedAt"/>, <see cref="CompletedAt"/> and <see cref="Error"/>; not stored.</summary>
    public StepRunState State => this switch
    {
        { Error: not null } => StepRunState.Failed,
        { CompletedAt: not null } => StepRunState.Succeeded,
        { StartedAt: not null } => StepRunState.Running,
        _ => StepRunState.Pending,
    };

    public string RenderedPrompt { get; set; } = string.Empty;

    public string CommandLine { get; set; } = string.Empty;

    public string RawOutputJson { get; set; } = string.Empty;

    public string ResultText { get; set; } = string.Empty;

    public string StdErr { get; set; } = string.Empty;

    public int? ExitCode { get; set; }

    public string? AgentSessionId { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>Stored as REAL: SQLite cannot sum or sort decimals stored as text.</summary>
    public double? CostUsd { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Error { get; set; }
}
