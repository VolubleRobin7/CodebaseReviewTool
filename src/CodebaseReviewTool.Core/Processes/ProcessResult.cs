namespace CodebaseReviewTool.Core.Processes;

/// <summary>Outcome of one external process run.</summary>
/// <param name="ExitCode">Process exit code; -1 when the process was killed on timeout.</param>
/// <param name="StandardOutput">Everything written to stdout.</param>
/// <param name="StandardError">Everything written to stderr.</param>
/// <param name="Duration">Wall-clock time from start to exit (or kill).</param>
/// <param name="TimedOut">True when the timeout elapsed and the process tree was killed.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
