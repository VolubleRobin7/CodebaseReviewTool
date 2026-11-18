using CodebaseReviewTool.Agents.Claude;
using CodebaseReviewTool.Application.Reviews;
using CodebaseReviewTool.Application.Usage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Host.Hosting;

/// <summary>
/// Keeps the home page's usage meter current through <see cref="AgentUsageService"/>: once at startup, then every
/// <c>Claude:UsageRefreshMinutes</c>, and sooner after a review changes because reviews are what use the limits up.
/// Requests in a burst collapse, and readings are at least <see cref="_minimumGap"/> apart.
/// </summary>
public sealed class AgentUsageRefreshService(
    AgentUsageService usage,
    ReviewEvents reviewEvents,
    IOptions<ClaudeOptions> options) : BackgroundService
{
    private static readonly TimeSpan _minimumGap = TimeSpan.FromSeconds(30);

    private readonly AgentUsageService _usage = usage;
    private readonly ReviewEvents _reviewEvents = reviewEvents;
    private readonly ClaudeOptions _options = options.Value;

    // Count 0 = nothing requested, 1 = a refresh is pending; a second request while one is pending is a no-op.
    private readonly SemaphoreSlim _requested = new(0, 1);

    public override void Dispose()
    {
        _requested.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _reviewEvents.ReviewChanged += OnReviewChanged;
        try
        {
            // Let the host finish starting before the first reading.
            await Task.Yield();

            var interval = TimeSpan.FromMinutes(_options.UsageRefreshMinutes);
            while (!stoppingToken.IsCancellationRequested)
            {
                await _usage.RefreshAsync(stoppingToken);

                await Task.Delay(_minimumGap, stoppingToken);
                await _requested.WaitAsync(interval - _minimumGap, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        finally
        {
            _reviewEvents.ReviewChanged -= OnReviewChanged;
        }
    }

    private void OnReviewChanged(ReviewChangedEventArgs args)
    {
        if (_requested.CurrentCount == 0)
        {
            try
            {
                _requested.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another caller got in between the check and the release; one pending refresh is all that is needed.
            }
            catch (ObjectDisposedException)
            {
                // The host is stopping.
            }
        }
    }
}
