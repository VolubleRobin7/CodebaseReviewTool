namespace CodebaseReviewTool.Core.Agents;

/// <summary>One prompt for the agent to run.</summary>
/// <param name="Prompt">The rendered prompt.</param>
/// <param name="WorkingDirectory">Directory the agent runs in (the review's worktree).</param>
/// <param name="Model">Model name, or null for the agent's default.</param>
/// <param name="Timeout">After this the agent is stopped and the run fails.</param>
/// <param name="MaxTurns">Limit on agent turns, or null for the agent's default.</param>
/// <param name="AllowedTools">Tools the agent may use without asking.</param>
/// <param name="JsonSchema">JSON schema the final answer must match, or null for free text.</param>
public sealed record AgentRequest(
    string Prompt,
    string WorkingDirectory,
    string? Model,
    TimeSpan Timeout,
    int? MaxTurns,
    IReadOnlyList<string> AllowedTools,
    string? JsonSchema);
