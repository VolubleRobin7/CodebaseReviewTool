using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>The boundary to the AI tool that runs the flow steps.</summary>
public interface IAgentRunner
{
    /// <summary>
    /// Runs the prompt to completion. Failures of the run itself (non-zero exit, timeout, an error reported by the
    /// agent, unreadable output) are returned in the result, not thrown. Cancelling <paramref name="cancellationToken"/>
    /// stops the agent and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reads how much of the plan's limits is used, in whatever way the tool offers. Failures (the tool not answering,
    /// unreadable output) are returned in the result, not thrown; a tool with no limits returns an empty list.
    /// </summary>
    Task<ServiceResult<AgentUsage>> GetUsageAsync(CancellationToken cancellationToken);
}
