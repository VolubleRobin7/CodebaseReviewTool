namespace CodebaseReviewTool.Core.Processes;

/// <summary>What to run.</summary>
/// <param name="FileName">Executable path, or a bare name resolved through PATH.</param>
/// <param name="Arguments">Arguments, passed individually (no shell quoting needed).</param>
/// <param name="WorkingDirectory">Working directory; null means the current directory.</param>
/// <param name="StandardInput">Text written to stdin, which is then closed. Null leaves stdin unredirected.</param>
/// <param name="Timeout">After this the whole process tree is killed and the result is marked timed out. Null means no timeout.</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    string? StandardInput = null,
    TimeSpan? Timeout = null);
