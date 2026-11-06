namespace CodebaseReviewTool.Core.Hosting;

/// <summary>
/// Lets the user pause the scheduled DevOps polls, for example while working offline. A refresh asked for through
/// <see cref="IPollTrigger"/> still polls while paused. The setting lives in memory and resets on restart.
/// </summary>
public interface IPollPause
{
    /// <summary>True while scheduled polls are skipped.</summary>
    bool IsPaused { get; }

    /// <summary>Pauses or resumes the scheduled polls. Resuming polls straight away so the list catches up.</summary>
    void SetPaused(bool paused);

    /// <summary>
    /// Raised when <see cref="IsPaused"/> changes, on the caller's thread; Blazor components must marshal with
    /// <c>InvokeAsync</c>.
    /// </summary>
    event Action? Changed;
}

/// <summary>The in-memory <see cref="IPollPause"/> that <see cref="DevOpsPollService"/> reads before each scheduled poll.</summary>
public sealed class PollPause(IPollTrigger trigger) : IPollPause
{
    private readonly IPollTrigger _trigger = trigger;
    private readonly Lock _lock = new();
    private bool _isPaused;

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _isPaused;
            }
        }
    }

    public event Action? Changed;

    public void SetPaused(bool paused)
    {
        lock (_lock)
        {
            if (_isPaused == paused)
            {
                return;
            }

            _isPaused = paused;
        }

        if (!paused)
        {
            _trigger.RequestPoll();
        }

        // A subscriber's failure must never reach the caller, so each handler is isolated.
        foreach (var handler in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try
            {
                handler();
            }
            catch (Exception)
            {
                // Pause notifications are best effort; subscribers read IsPaused when they next render.
            }
        }
    }
}
