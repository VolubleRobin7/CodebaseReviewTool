using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>
/// The boundary to one AI tool (Claude Code today). Every implementation is registered against this interface and
/// collected by <see cref="Agents.IAgentRegistry"/>, which finds one by <see cref="Key"/>.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Identifies the agent, e.g. "claude". Anything that stores it relies on it never changing once released.
    /// Compared case-insensitively.
    /// </summary>
    string Key { get; }

    /// <summary>The agent's name as the UI shows it, e.g. "Claude Code".</summary>
    string DisplayName { get; }

    /// <summary>
    /// The models a prompt can ask for, each with the effort levels it accepts. The first is the agent's default.
    /// </summary>
    IReadOnlyList<AgentModel> Models { get; }

    /// <summary>
    /// Checks the agent is installed and answering. Returns what it reported (its version, say) on success and why it
    /// is not usable on failure; never throws for an expected failure.
    /// </summary>
    Task<ServiceResult<string>> HealthCheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the prompt to completion, or continues an earlier conversation when the request names one. A failed run
    /// (a timeout, an error reported by the agent, an answer that cannot be read) is returned with
    /// <see cref="PromptResult.Error"/> set, not thrown. Cancelling <paramref name="cancellationToken"/> stops the agent
    /// and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<PromptResult> RunPromptAsync(PromptRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads how much of each of the plan's limits is used, in whatever way the agent offers. A failure (the agent not
    /// answering, an answer that cannot be read) is returned in the result, not thrown; an agent with no limits returns
    /// an empty list.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<AgentUsageLimit>>> GetUsageAsync(CancellationToken cancellationToken);
}
