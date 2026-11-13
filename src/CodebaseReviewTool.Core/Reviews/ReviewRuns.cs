namespace CodebaseReviewTool.Core.Reviews;

/// <summary>
/// The reviews the queue service is processing, each with its own cancellation so one review can be stopped without
/// stopping the others. Also remembers reviews cancelled while still waiting in the queue, so the queue service skips
/// them when their turn comes. Both sides take the same lock, so a review is either stopped while running or skipped
/// before it starts, never neither.
/// </summary>
public sealed class ReviewRuns
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, ReviewRun> _running = [];
    private readonly HashSet<int> _skipWhenDequeued = [];

    /// <summary>
    /// Registers a review the queue service is about to process. Returns null when the review was cancelled while it
    /// waited, in which case it must be skipped. Dispose the result once processing has ended.
    /// </summary>
    public ReviewRun? Begin(int reviewId, CancellationToken stoppingToken)
    {
        lock (_lock)
        {
            if (_skipWhenDequeued.Remove(reviewId))
            {
                return null;
            }

            var run = new ReviewRun(this, reviewId, stoppingToken);
            _running[reviewId] = run;
            return run;
        }
    }

    /// <summary>
    /// Stops the review. When it is being processed, its run is cancelled and the returned task completes once the run
    /// has ended. Otherwise, when <paramref name="isQueued"/> is true, the queue service is told to skip it; the
    /// returned task is already complete.
    /// </summary>
    public Task CancelAsync(int reviewId, bool isQueued)
    {
        ReviewRun? run;
        lock (_lock)
        {
            if (!_running.TryGetValue(reviewId, out run))
            {
                if (isQueued)
                {
                    _skipWhenDequeued.Add(reviewId);
                }

                return Task.CompletedTask;
            }
        }

        return run.CancelAsync();
    }

    internal void End(ReviewRun run)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(run.ReviewId, out var current) && current == run)
            {
                _running.Remove(run.ReviewId);
            }
        }
    }
}

/// <summary>One review being processed by the queue service; see <see cref="ReviewRuns"/>.</summary>
public sealed class ReviewRun : IDisposable
{
    private readonly ReviewRuns _owner;
    private readonly CancellationTokenSource _cancellation;
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _isCancelledByUser;

    internal ReviewRun(ReviewRuns owner, int reviewId, CancellationToken stoppingToken)
    {
        _owner = owner;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        ReviewId = reviewId;
    }

    public int ReviewId { get; }

    /// <summary>Cancelled when the user stops the review or the service stops.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>True once the user has stopped the review; whoever stopped it records the outcome.</summary>
    public bool IsCancelledByUser => _isCancelledByUser;

    public void Dispose()
    {
        _owner.End(this);
        _cancellation.Dispose();
        _ended.TrySetResult();
    }

    internal async Task CancelAsync()
    {
        _isCancelledByUser = true;
        try
        {
            // CancelAsync runs the token's callbacks (killing the agent process) off the caller's thread.
            await _cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The run ended on its own in the meantime.
        }

        await _ended.Task;
    }
}
