namespace CodebaseReviewTool.Core.Agents;

/// <summary>Outcome of one agent run.</summary>
/// <param name="Succeeded">True when the agent exited cleanly and reported success.</param>
/// <param name="ResultText">The agent's final answer as text; empty when there is none.</param>
/// <param name="StructuredOutputJson">The answer validated against the request's schema, as JSON; null when not requested or not returned.</param>
/// <param name="SessionId">The agent's session id, which can be used to resume or inspect the run.</param>
/// <param name="CostUsd">Cost the agent reported, in US dollars.</param>
/// <param name="DurationMs">Wall-clock time of the run as the agent reported it, or as measured when it did not.</param>
/// <param name="RawOutput">Everything the agent wrote to stdout.</param>
/// <param name="StandardError">Everything the agent wrote to stderr.</param>
/// <param name="ExitCode">Process exit code; null when the run was stopped on timeout.</param>
/// <param name="TimedOut">True when the run exceeded its timeout and was stopped.</param>
/// <param name="Error">Why the run failed; null when it succeeded.</param>
/// <param name="CommandLine">The command line used, without the prompt (which goes through stdin).</param>
public sealed record AgentResult(
    bool Succeeded,
    string ResultText,
    string? StructuredOutputJson,
    string? SessionId,
    double? CostUsd,
    long DurationMs,
    string RawOutput,
    string StandardError,
    int? ExitCode,
    bool TimedOut,
    string? Error,
    string CommandLine);
