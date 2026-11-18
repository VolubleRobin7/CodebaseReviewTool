using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Application.Usage;

/// <summary>
/// The latest reading of the AI tool's plan usage, for the home page's usage meter. Host's usage refresher decides when
/// to call <see cref="RefreshAsync"/>; the tool decides how the usage is read. A failed reading is logged and kept on
/// <see cref="LastError"/>, and the previous reading stays.
/// </summary>
public sealed class AgentUsageService(IAgentRunner agentRunner, ILogger<AgentUsageService> logger)
{
    private readonly IAgentRunner _agentRunner = agentRunner;
    private readonly ILogger<AgentUsageService> _logger = logger;

    private volatile AgentUsage? _latest;
    private volatile string? _lastError;

    /// <summary>The latest successful reading; null until the first one.</summary>
    public AgentUsage? Latest => _latest;

    /// <summary>Why the latest reading failed; null when it succeeded or none has run.</summary>
    public string? LastError => _lastError;

    /// <summary>
    /// Raised after every reading, on the refresher's thread; Blazor components must marshal with <c>InvokeAsync</c>.
    /// </summary>
    public event Action? Changed;

    /// <summary>Reads the usage from the AI tool and raises <see cref="Changed"/>.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ServiceResult<AgentUsage> result;
        try
        {
            result = await _agentRunner.GetUsageAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = ServiceResult<AgentUsage>.Failure($"{ex.GetType().Name}: {ex.Message}");
        }

        if (result.Succeeded)
        {
            _latest = result.Value;
            _lastError = null;
            _logger.LogDebug("Agent usage read: {Limits}", string.Join("; ", result.Value!.Limits.Select(l => $"{l.Name} {l.PercentUsed}%")));
        }
        else
        {
            _lastError = result.Error;
            _logger.LogWarning("Reading the agent's usage failed; keeping the previous reading. {Error}", result.Error);
        }

        RaiseChanged();
    }

    // A subscriber's failure must never reach the refresher, so each handler is isolated.
    private void RaiseChanged()
    {
        foreach (var handler in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                handler();
            }
            catch (Exception)
            {
                // Notifications are best effort; subscribers read the state when they next render.
            }
        }
    }
}
