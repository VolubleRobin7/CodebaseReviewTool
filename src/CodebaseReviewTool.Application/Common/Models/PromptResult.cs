namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>Outcome of one prompt. A failed run still carries whatever the agent produced, for troubleshooting.</summary>
/// <param name="Error">Why the run failed (a timeout is one reason); null when it succeeded.</param>
/// <param name="Text">The agent's answer as text; empty when there is none.</param>
/// <param name="StructuredOutputJson">
/// The answer as JSON that matches the request's schema; null when no schema was given or the agent returned none, in
/// which case the answer may still be found in <paramref name="Text"/>.
/// </param>
/// <param name="ConversationId">The conversation the prompt ran in, which a later request can continue; null when the agent gives none.</param>
/// <param name="Model">The model that ran the prompt, as the agent names it; null when the agent does not say.</param>
/// <param name="Effort">The effort level the prompt ran at, as the agent names it; null when the agent does not say.</param>
/// <param name="CostUsd">Cost the agent reported, in US dollars; null when it reports none.</param>
/// <param name="Duration">How long the run took, as the agent reported it or as measured when it did not.</param>
/// <param name="RawOutput">Everything the agent produced, unprocessed.</param>
/// <param name="Diagnostics">
/// Agent-specific detail for troubleshooting, such as Claude Code's command line, exit code and error output; null when
/// there is none.
/// </param>
public sealed record PromptResult(
    string? Error,
    string Text,
    string? StructuredOutputJson,
    string? ConversationId,
    string? Model,
    string? Effort,
    double? CostUsd,
    TimeSpan Duration,
    string RawOutput,
    string? Diagnostics)
{
    /// <summary>True when the run completed and the agent reported success.</summary>
    public bool Succeeded => Error is null;
}
