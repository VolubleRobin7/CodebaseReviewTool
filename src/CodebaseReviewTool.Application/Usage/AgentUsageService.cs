using CodebaseReviewTool.Application.Agents;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Application.Usage;

/// <summary>The latest usage reading of one agent.</summary>
/// <param name="AgentKey">The agent's <see cref="IAgent.Key"/>.</param>
/// <param name="DisplayName">The agent's name as the UI shows it.</param>
/// <param name="Limits">The plan's limits at the latest successful reading; null until the first one, empty when the agent has none.</param>
/// <param name="CheckedUtc">When the latest successful reading was taken; null until the first one.</param>
/// <param name="LastError">Why the latest reading failed; null when it succeeded or none has run.</param>
public sealed record AgentUsageReading(
    string AgentKey,
    string DisplayName,
    IReadOnlyList<AgentUsageLimit>? Limits,
    DateTime? CheckedUtc,
    string? LastError)
{
    /// <summary>The limit closest to running out, which is the one that will stop reviews first; null when there is none.</summary>
    public AgentUsageLimit? Highest => Limits?.MaxBy(l => l.PercentUsed);
}

/// <summary>
/// The latest reading of every agent's plan usage, for the home page's usage meter. Host's usage refresher decides when
/// to call <see cref="RefreshAsync"/>; each agent decides how its usage is read. A failed reading is logged and kept on
/// <see cref="AgentUsageReading.LastError"/>, and the agent's previous reading stays.
/// </summary>
public sealed class AgentUsageService
{
    private readonly IAgentRegistry _agents;
    private readonly ILogger<AgentUsageService> _logger;

    private volatile IReadOnlyList<AgentUsageReading> _readings;

    public AgentUsageService(IAgentRegistry agents, ILogger<AgentUsageService> logger)
    {
        ArgumentNullException.ThrowIfNull(agents);

        _agents = agents;
        _logger = logger;
        _readings = [.. agents.Agents.Select(a => new AgentUsageReading(a.Key, a.DisplayName, null, null, null))];
    }

    /// <summary>One reading per agent, in registration order.</summary>
    public IReadOnlyList<AgentUsageReading> Readings => _readings;

    /// <summary>
    /// Raised after every refresh, on the refresher's thread; Blazor components must marshal with <c>InvokeAsync</c>.
    /// </summary>
    public event Action? Changed;

    /// <summary>Reads the usage from every agent, one after another, and raises <see cref="Changed"/>.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var readings = new List<AgentUsageReading>();
        foreach (var (agent, previous) in _agents.Agents.Zip(_readings))
        {
            readings.Add(await ReadAsync(agent, previous, cancellationToken));
        }

        _readings = readings;
        RaiseChanged();
    }

    private async Task<AgentUsageReading> ReadAsync(IAgent agent, AgentUsageReading previous, CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<AgentUsageLimit>> result;
        try
        {
            result = await agent.GetUsageAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = ServiceResult<IReadOnlyList<AgentUsageLimit>>.Failure($"{ex.GetType().Name}: {ex.Message}");
        }

        if (result.Succeeded)
        {
            _logger.LogDebug(
                "{Agent} usage read: {Limits}", agent.Key, string.Join("; ", result.Value!.Select(l => $"{l.Name} {l.PercentUsed}%")));
            return previous with { Limits = result.Value, CheckedUtc = DateTime.UtcNow, LastError = null };
        }

        _logger.LogWarning("Reading {Agent} usage failed; keeping the previous reading. {Error}", agent.Key, result.Error);
        return previous with { LastError = result.Error };
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
