namespace CodebaseReviewTool.Core.Data.Enums;

/// <summary>
/// State of one step run. Never stored: <see cref="Entities.ReviewStepRun.State"/> derives it from the run's
/// timestamps and error.
/// </summary>
public enum StepRunState
{
    /// <summary>Row exists but the process has not started.</summary>
    Pending,

    /// <summary>Started and not yet finished.</summary>
    Running,

    /// <summary>Finished without error.</summary>
    Succeeded,

    /// <summary>Finished with an error recorded.</summary>
    Failed,
}
