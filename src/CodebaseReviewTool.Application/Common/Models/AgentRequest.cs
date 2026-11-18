namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>One prompt for an AI agent to run. Anything left null falls back to that agent's own configuration.</summary>
/// <param name="Prompt">The rendered prompt.</param>
/// <param name="WorkingDirectory">Directory the agent runs in (the review's worktree).</param>
/// <param name="Model">Model name, or null for the agent's configured default.</param>
/// <param name="Timeout">After this the agent is stopped and the run fails; null for the agent's configured default.</param>
/// <param name="JsonSchema">JSON schema the final answer must match, or null for free text.</param>
public sealed record AgentRequest(
    string Prompt,
    string WorkingDirectory,
    string? Model,
    TimeSpan? Timeout,
    string? JsonSchema);
