namespace CodebaseReviewTool.Core.Agents;

/// <summary>The boundary to the AI tool that runs one flow step.</summary>
public interface IAgentRunner
{
    /// <summary>
    /// Runs the prompt to completion. Failures of the run itself (non-zero exit, timeout, an error reported by the
    /// agent, unreadable output) are returned in the result, not thrown. Cancelling <paramref name="cancellationToken"/>
    /// stops the agent and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken cancellationToken);
}
