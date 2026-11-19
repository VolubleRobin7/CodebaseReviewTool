using System.Diagnostics.CodeAnalysis;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Configuration;

namespace CodebaseReviewTool.Application.Agents;

/// <summary>Every registered <see cref="IAgent"/>, found by its key.</summary>
public interface IAgentRegistry
{
    /// <summary>Every agent, in registration order; never empty.</summary>
    IReadOnlyList<IAgent> Agents { get; }

    /// <summary>The agent every flow step runs on for now: the first one registered.</summary>
    IAgent Default { get; }

    /// <summary>Finds an agent by its <see cref="IAgent.Key"/> (case-insensitive).</summary>
    bool TryGet(string? key, [NotNullWhen(true)] out IAgent? agent);
}

/// <summary>
/// Built from every <see cref="IAgent"/> registration. Throws <see cref="StartupValidationException"/> when there is
/// none, when two share a key or when one offers no models; <c>StartupInitializer</c> resolves it first, so that stops
/// the host at startup.
/// </summary>
public sealed class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<string, IAgent> _byKey;

    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        Agents = [.. agents];
        if (Agents.Count == 0)
        {
            throw new StartupValidationException(["No AI agent is configured. Add an agent's section to appsettings.json."]);
        }

        var failures = Agents
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"More than one agent uses the key '{g.Key}'.")
            .Concat(Agents.Where(a => a.Models.Count == 0).Select(a => $"{a.DisplayName} offers no models."))
            .ToList();

        if (failures.Count > 0)
        {
            throw new StartupValidationException(failures);
        }

        _byKey = Agents.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgent> Agents { get; }

    public IAgent Default => Agents[0];

    public bool TryGet(string? key, [NotNullWhen(true)] out IAgent? agent)
    {
        agent = null;
        return key is not null && _byKey.TryGetValue(key, out agent);
    }
}
