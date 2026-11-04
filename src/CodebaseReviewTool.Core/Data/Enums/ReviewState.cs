namespace CodebaseReviewTool.Core.Data.Enums;

/// <summary>
/// Where a review is in its lifecycle. Never stored: <see cref="Entities.Review.State"/> derives it from the
/// review's timestamps, error and step runs.
/// </summary>
public enum ReviewState
{
    /// <summary>Created but not yet picked up by the queue.</summary>
    Queued,

    /// <summary>Picked up; the worktree does not exist yet.</summary>
    PreparingWorktree,

    /// <summary>The worktree exists and a step is running or about to run.</summary>
    Running,

    /// <summary>Every step finished without error.</summary>
    Completed,

    /// <summary>The review or one of its steps recorded an error.</summary>
    Failed,
}
