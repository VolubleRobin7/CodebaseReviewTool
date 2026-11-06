using System.Collections.Concurrent;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>How the last DevOps poll of one repository went. All times are UTC.</summary>
/// <param name="RepositoryKey">The repository polled.</param>
/// <param name="LastSuccessfulPollUtc">When the repository's pull requests were last read and merged; null if never.</param>
/// <param name="LastError">The error from the latest attempt, or null when it succeeded.</param>
public sealed record RepositoryPollStatus(string RepositoryKey, DateTime? LastSuccessfulPollUtc, string? LastError);

/// <summary>
/// The UI's view of the DevOps poller: what it is doing, how its last polls went, and the two things the user can ask
/// of it, a poll now and a pause of the scheduled polls.
/// </summary>
public interface IPollControl
{
    /// <summary>False when there are no DevOps credentials, so the poller never runs.</summary>
    bool IsEnabled { get; }

    /// <summary>True while a poll is in progress.</summary>
    bool IsPolling { get; }

    /// <summary>True while scheduled polls are skipped. Requested polls still run. Resets on restart.</summary>
    bool IsPaused { get; }

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
    /// Raised when a poll starts or finishes or the pause changes, on the caller's or the poller's thread; Blazor
    /// components must marshal with <c>InvokeAsync</c>.
    /// </summary>
    event Action? Changed;

    /// <summary>Requests a poll now, paused or not. Requests made while one is pending collapse into a single poll.</summary>
    void RequestPoll();

    /// <summary>Pauses or resumes the scheduled polls. Resuming polls straight away so the list catches up.</summary>
    void SetPaused(bool paused);
}

/// <summary>
/// The poller's shared state. <see cref="DevOpsPollService"/> writes the status through the internal methods and waits
/// here between polls, waking early when one is requested; everything else sees it as <see cref="IPollControl"/>.
/// </summary>
public sealed class PollControl : IPollControl, IDisposable
{
    private readonly ConcurrentDictionary<string, RepositoryPollStatus> _repositories = new(StringComparer.OrdinalIgnoreCase);

    // Count 0 = nothing requested, 1 = a poll is pending; a second request while one is pending is a no-op.
    private readonly SemaphoreSlim _requested = new(0, 1);

    private volatile bool _isEnabled = true;
    private volatile bool _isPolling;
    private volatile bool _isPaused;

    public bool IsEnabled => _isEnabled;

    public bool IsPolling => _isPolling;

    public bool IsPaused => _isPaused;

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

    public void RequestPoll()
    {
        if (_requested.CurrentCount == 0)
        {
            try
            {
                _requested.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another caller got in between the check and the release; one pending poll is all that is needed.
            }
        }
    }

    public void SetPaused(bool paused)
    {
        if (_isPaused == paused)
        {
            return;
        }

        _isPaused = paused;
        if (!paused)
        {
            RequestPoll();
        }

        RaiseChanged();
    }

    public void Dispose() => _requested.Dispose();

    /// <summary>Returns true when a poll was requested within <paramref name="interval"/>, false when it elapsed.</summary>
    internal Task<bool> WaitAsync(TimeSpan interval, CancellationToken cancellationToken) =>
        _requested.WaitAsync(interval, cancellationToken);

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

    // A subscriber's failure must never reach the poller or the caller, so each handler is isolated.
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
