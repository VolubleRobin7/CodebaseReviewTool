namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>One limit of the AI agent's plan.</summary>
/// <param name="Name">The limit's name as the agent words it, e.g. "Current session" or "Current week (all models)".</param>
/// <param name="PercentUsed">How much of the limit is used, 0 to 100.</param>
/// <param name="ResetsAt">When the limit resets, in UTC; null when the agent does not say or it cannot be read.</param>
public sealed record AgentUsageLimit(string Name, double PercentUsed, DateTime? ResetsAt);
