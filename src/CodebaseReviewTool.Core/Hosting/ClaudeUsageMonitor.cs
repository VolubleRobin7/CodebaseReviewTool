using System.Text.Json.Nodes;
using CodebaseReviewTool.Core.Agents;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Processes;
using CodebaseReviewTool.Core.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>The UI's view of how much of the Claude plan's limits is used.</summary>
public interface IClaudeUsage
{
    /// <summary>The latest successful reading; null until the first one.</summary>
    ClaudeUsageSnapshot? Snapshot { get; }

    /// <summary>Why the latest reading failed; null when it succeeded or none has run.</summary>
    string? LastError { get; }

    /// <summary>
    /// Raised after every reading, on the monitor's thread; Blazor components must marshal with <c>InvokeAsync</c>.
    /// </summary>
    event Action? Changed;
}

/// <summary>
/// Reads the plan's usage with <c>claude -p /usage --output-format json</c>, a local command that costs no tokens:
/// once at startup, then every <c>Claude:UsageRefreshMinutes</c>, and sooner after a review changes because reviews
/// are what use the limits up. Requests in a burst collapse, and runs are at least <see cref="_minimumGap"/> apart.
/// A failure is logged and kept on <see cref="LastError"/>; the previous reading stays.
/// </summary>
public sealed class ClaudeUsageMonitor(
    IProcessRunner processRunner,
    ReviewEvents reviewEvents,
    IOptions<ClaudeOptions> options,
    ILogger<ClaudeUsageMonitor> logger) : BackgroundService, IClaudeUsage
{
    private static readonly TimeSpan _minimumGap = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    private readonly IProcessRunner _processRunner = processRunner;
    private readonly ReviewEvents _reviewEvents = reviewEvents;
    private readonly ClaudeOptions _options = options.Value;
    private readonly ILogger<ClaudeUsageMonitor> _logger = logger;

    // Count 0 = nothing requested, 1 = a refresh is pending; a second request while one is pending is a no-op.
    private readonly SemaphoreSlim _requested = new(0, 1);

    private volatile ClaudeUsageSnapshot? _snapshot;
    private volatile string? _lastError;

    public ClaudeUsageSnapshot? Snapshot => _snapshot;

    public string? LastError => _lastError;

    public event Action? Changed;

    public override void Dispose()
    {
        _requested.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (EF.IsDesignTime)
        {
            return;
        }

        _reviewEvents.ReviewChanged += OnReviewChanged;
        try
        {
            // Let the host finish starting before the first run of the CLI.
            await Task.Yield();

            var interval = TimeSpan.FromMinutes(_options.UsageRefreshMinutes);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RefreshAsync(stoppingToken);

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

    private void OnReviewChanged(ReviewChangedEventArgs args) => RequestRefresh();

    private void RequestRefresh()
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

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            // An empty stdin is closed at once, so the CLI never waits on it. The working directory is irrelevant to a
            // local command; the temp folder keeps it out of any repository's project settings.
            var process = await _processRunner.RunAsync(
                new ProcessRequest(_options.ExecutablePath, ["-p", "/usage", "--output-format", "json"], Path.GetTempPath(), string.Empty, _timeout),
                cancellationToken);

            var output = ClaudeCodeRunner.FindResultObject(process.StandardOutput);
            var text = output?["result"] is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;
            var isError = output?["is_error"] is JsonValue flag && flag.TryGetValue<bool>(out var error) && error;

            if (process.TimedOut)
            {
                Fail($"/usage did not finish within {_timeout.TotalSeconds:0} seconds.");
            }
            else if (text is null || isError)
            {
                var detail = text ?? process.StandardError;
                Fail($"/usage failed (exit code {process.ExitCode}). {detail.Trim()}".Trim());
            }
            else
            {
                _snapshot = ClaudeUsageSnapshot.Parse(text, DateTime.UtcNow);
                _lastError = null;
                _logger.LogDebug("Claude usage read: {Limits}", string.Join("; ", _snapshot.Limits.Select(l => $"{l.Name} {l.PercentUsed}%")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }

        RaiseChanged();
    }

    private void Fail(string error)
    {
        _lastError = error;
        _logger.LogWarning("Reading Claude usage failed; keeping the previous reading. {Error}", error);
    }

    // A subscriber's failure must never reach the monitor, so each handler is isolated.
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
