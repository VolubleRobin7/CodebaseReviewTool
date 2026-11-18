namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>One limit of the AI tool's plan.</summary>
/// <param name="Name">The limit's name as the tool words it, e.g. "Current session" or "Current week (all models)".</param>
/// <param name="PercentUsed">How much of the limit is used, 0 to 100.</param>
/// <param name="Resets">When the limit resets, as the tool words it; null when not given.</param>
public sealed record AgentUsageLimit(string Name, double PercentUsed, string? Resets);

/// <summary>The plan limits read at one moment. Empty when the tool has no plan limits (Claude Code on an API key).</summary>
/// <param name="Limits">Every limit listed, in the tool's order.</param>
/// <param name="CheckedUtc">When they were read.</param>
public sealed record AgentUsage(IReadOnlyList<AgentUsageLimit> Limits, DateTime CheckedUtc)
{
    /// <summary>The rolling session limit, which is the one a run of reviews uses up first; null when not listed.</summary>
    public AgentUsageLimit? Session =>
        Limits.FirstOrDefault(l => l.Name.Contains("session", StringComparison.OrdinalIgnoreCase));
}
