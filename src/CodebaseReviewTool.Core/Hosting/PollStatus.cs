using System.Collections.Concurrent;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>How the last DevOps poll of one repository went. All times are UTC.</summary>
/// <param name="RepositoryKey">The repository polled.</param>
/// <param name="LastSuccessfulPollUtc">When the repository's pull requests were last read and merged; null if never.</param>
/// <param name="LastError">The error from the latest attempt, or null when it succeeded.</param>
public sealed record RepositoryPollStatus(string RepositoryKey, DateTime? LastSuccessfulPollUtc, string? LastError);

/// <summary>What the DevOps poller is doing and how its last polls went, for the UI.</summary>
public interface IPollStatus
{
    /// <summary>False when there are no DevOps credentials, so the poller never runs.</summary>
    bool IsEnabled { get; }

    /// <summary>True while a poll is in progress.</summary>
    bool IsPolling { get; }

    /// <summary>
    /// The oldest of the repositories' last successful polls, i.e. how fresh the whole list is at worst. Null until
    /// every repository has been polled successfully at least once.
    /// </summary>
    DateTime? LastSuccessfulPollUtc { get; }

    /// <summary>The errors from each repository's latest attempt, prefixed with its key; null when none failed.</summary>
    string? LastError { get; }

    /// <summary>One entry per repository polled so far.</summary>
    IReadOnlyList<RepositoryPollStatus> Repositories { get; }

    /// <summary>
    /// Raised when a poll starts or finishes, on the poller's thread; Blazor components must marshal with
    /// <c>InvokeAsync</c>.
    /// </summary>
    event Action? Changed;
}

/// <summary>The poller's writable side of <see cref="IPollStatus"/>.</summary>
public sealed class PollStatus : IPollStatus
{
    private readonly ConcurrentDictionary<string, RepositoryPollStatus> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _isEnabled = true;
    private volatile bool _isPolling;

    public bool IsEnabled => _isEnabled;

    public bool IsPolling => _isPolling;

    public DateTime? LastSuccessfulPollUtc
    {
        get
        {
            var statuses = _repositories.Values.ToList();
            return statuses.Count == 0 || statuses.Any(s => s.LastSuccessfulPollUtc is null)
                ? null
                : statuses.Min(s => s.LastSuccessfulPollUtc);
        }
    }

    public string? LastError
    {
        get
        {
            var errors = _repositories.Values
                .Where(s => s.LastError is not null)
                .OrderBy(s => s.RepositoryKey, StringComparer.OrdinalIgnoreCase)
                .Select(s => $"{s.RepositoryKey}: {s.LastError}")
                .ToList();
            return errors.Count == 0 ? null : string.Join("; ", errors);
        }
    }

    public IReadOnlyList<RepositoryPollStatus> Repositories =>
        [.. _repositories.Values.OrderBy(s => s.RepositoryKey, StringComparer.OrdinalIgnoreCase)];

    public event Action? Changed;

    internal void Disable()
    {
        _isEnabled = false;
        RaiseChanged();
    }

    internal void PollStarted()
    {
        _isPolling = true;
        RaiseChanged();
    }

    internal void PollFinished()
    {
        _isPolling = false;
        RaiseChanged();
    }

    internal void RecordSuccess(string repositoryKey, DateTime pollUtc) =>
        _repositories[repositoryKey] = new RepositoryPollStatus(repositoryKey, pollUtc, null);

    internal void RecordFailure(string repositoryKey, string error) =>
        _repositories.AddOrUpdate(
            repositoryKey,
            key => new RepositoryPollStatus(key, null, error),
            (_, previous) => previous with { LastError = error });

    // A subscriber's failure must never reach the poller, so each handler is isolated.
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
                // Status notifications are best effort; the next poll raises another.
            }
        }
    }
}
