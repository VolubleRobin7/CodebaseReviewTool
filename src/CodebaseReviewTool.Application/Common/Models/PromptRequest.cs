namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>One prompt for an AI agent to run.</summary>
/// <param name="Prompt">The rendered prompt.</param>
/// <param name="WorkingDirectory">Directory the agent works in, which holds the code it is asked about (the review's worktree).</param>
/// <param name="Model">The <see cref="AgentModel.Name"/> of one of the agent's <see cref="Interfaces.IAgent.Models"/>.</param>
/// <param name="Timeout">After this the agent is stopped and the run fails.</param>
/// <param name="Effort">
/// One of the model's <see cref="AgentModel.Efforts"/>, or null to leave it to the model's own default (the only choice
/// for a model that takes no effort setting).
/// </param>
/// <param name="JsonSchema">JSON schema the answer must match, or null for a free-text answer.</param>
/// <param name="ConversationId">
/// A <see cref="PromptResult.ConversationId"/> to continue that conversation, or null to start a new one. Agents may
/// only find a conversation from the same <paramref name="WorkingDirectory"/>.
/// </param>
public sealed record PromptRequest(
    string Prompt,
    string WorkingDirectory,
    string Model,
    TimeSpan Timeout,
    string? Effort = null,
    string? JsonSchema = null,
    string? ConversationId = null);
