namespace CodebaseReviewTool.Core.Hosting;

/// <summary>Lets the UI ask for a pull request poll now instead of waiting for the next scheduled one.</summary>
public interface IPollTrigger
{
    /// <summary>Requests a poll. Requests made while one is already pending collapse into a single poll.</summary>
    void RequestPoll();
}

/// <summary>
/// The poller's side of <see cref="IPollTrigger"/>: it waits here between polls, waking early when a poll is
/// requested.
/// </summary>
public sealed class PollTrigger : IPollTrigger, IDisposable
{
    // Count 0 = nothing requested, 1 = a poll is pending; a second request while one is pending is a no-op.
    private readonly SemaphoreSlim _requested = new(0, 1);

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

    /// <summary>Returns true when a poll was requested within <paramref name="interval"/>, false when it elapsed.</summary>
    internal Task<bool> WaitAsync(TimeSpan interval, CancellationToken cancellationToken) =>
        _requested.WaitAsync(interval, cancellationToken);

    public void Dispose() => _requested.Dispose();
}
