namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>A model an agent can run a prompt on.</summary>
/// <param name="Name">The model's name as the agent takes it, e.g. "opus".</param>
/// <param name="Efforts">
/// The effort levels the model accepts, as the agent names them (e.g. "low", "high"), lowest first; empty when the model
/// takes no effort setting.
/// </param>
public sealed record AgentModel(string Name, IReadOnlyList<string> Efforts);
