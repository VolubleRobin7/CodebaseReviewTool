using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Data.Entities;

/// <summary>One review run of one pull request. All times are UTC.</summary>
public class Review
{
    public int Id { get; set; }

    public int PullRequestId { get; set; }

    public PullRequest? PullRequest { get; set; }

    public string RepositoryKey { get; set; } = string.Empty;

    public string SourceCommit { get; set; } = string.Empty;

    public string MergeBaseCommit { get; set; } = string.Empty;

    /// <summary>
    /// Derived from the review's timestamps, <see cref="Error"/> and <see cref="StepRuns"/>; not stored. Load
    /// <see cref="StepRuns"/> before reading it, or a failed step is reported as running.
    /// </summary>
    public ReviewState State
    {
        get
        {
            if (Error is not null || StepRuns.Any(s => s.State == StepRunState.Failed))
            {
                return ReviewState.Failed;
            }

            if (CompletedAt is not null)
            {
                return ReviewState.Completed;
            }

            if (StartedAt is null)
            {
                return ReviewState.Queued;
            }

            return string.IsNullOrEmpty(WorktreePath) ? ReviewState.PreparingWorktree : ReviewState.Running;
        }
    }

    /// <summary>The step currently running, or null when no step is in flight. Needs <see cref="StepRuns"/> loaded.</summary>
    public int? CurrentStep => StepRuns.FirstOrDefault(s => s.State == StepRunState.Running)?.Step;

    public string WorktreePath { get; set; } = string.Empty;

    public DateTime? WorktreeRemovedAt { get; set; }

    public DateTime QueuedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Error { get; set; }

    public List<ReviewStepRun> StepRuns { get; set; } = [];

    public List<Issue> Issues { get; set; } = [];
}
