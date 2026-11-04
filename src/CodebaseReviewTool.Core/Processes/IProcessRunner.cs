namespace CodebaseReviewTool.Core.Processes;

/// <summary>The only place an external process is started. Git and the AI agent both go through it.</summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs the process to completion and captures its output. A timeout kills the process tree and returns a
    /// result with <see cref="ProcessResult.TimedOut"/> set; cancelling <paramref name="cancellationToken"/> kills
    /// the tree and throws <see cref="OperationCanceledException"/>. Failing to start (e.g. file not found) throws.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}
