using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.DevOps;
using CodebaseReviewTool.Core.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>
/// Keeps the pull request cache current: polls every configured repository once at startup, then every
/// <c>DevOps:PollIntervalSeconds</c> or sooner when <see cref="IPollTrigger.RequestPoll"/> asks for one. While
/// <see cref="IPollPause"/> is paused the scheduled polls are skipped and only requested ones run. Read-only towards
/// DevOps. A failing repository is logged and recorded on
/// <see cref="IPollStatus"/> and the cache keeps its last contents; it never stops the other repositories or the
/// service. Never starts a review.
/// </summary>
public sealed class DevOpsPollService(
    IServiceScopeFactory scopeFactory,
    IRepositoryRegistry registry,
    PullRequestCache cache,
    PollStatus status,
    PollTrigger trigger,
    IPollPause pause,
    IOptions<DevOpsOptions> options,
    ILogger<DevOpsPollService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly PullRequestCache _cache = cache;
    private readonly PollStatus _status = status;
    private readonly PollTrigger _trigger = trigger;
    private readonly IPollPause _pause = pause;
    private readonly DevOpsOptions _options = options.Value;
    private readonly ILogger<DevOpsPollService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (EF.IsDesignTime)
        {
            return;
        }

        if (!_options.HasCredentials)
        {
            _status.Disable();
            _logger.LogInformation("DevOps polling is disabled: DevOps:Pat is not set and DevOps:UseDefaultCredentials is false");
            return;
        }

        // Let the host finish starting before the first poll's network calls.
        await Task.Yield();

        var interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        var wake = PollWake.Requested;
        while (wake != PollWake.Stopping)
        {
            if (wake == PollWake.Requested || !_pause.IsPaused)
            {
                await PollAsync(stoppingToken);
            }
            else
            {
                _logger.LogDebug("Scheduled poll skipped: polling is paused");
            }

            wake = await WaitForNextPollAsync(interval, stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        _status.PollStarted();
        try
        {
            // The typed DevOps client is transient; one scope per poll gives it a fresh HttpClient from the factory.
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IDevOpsClient>();

            foreach (var repository in _registry.Repositories)
            {
                await PollRepositoryAsync(client, repository, cancellationToken);
            }
        }
        finally
        {
            _status.PollFinished();
        }
    }

    private async Task PollRepositoryAsync(IDevOpsClient client, ResolvedRepository repository, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.ListActivePullRequestsAsync(repository, cancellationToken);
            if (!result.Succeeded)
            {
                Fail(repository, result.Error ?? "Unknown error");
                return;
            }

            var merge = await _cache.MergeAsync(repository, result.Items, cancellationToken);
            _status.RecordSuccess(repository.Key, DateTime.UtcNow);
            _logger.LogInformation(
                "Polled {RepositoryKey}: {Active} active pull requests ({Added} added, {Updated} updated, {Closed} closed)",
                repository.Key, result.Items.Count, merge.Added, merge.Updated, merge.Closed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Database or unexpected failures: keep polling the other repositories and on the next tick.
            Fail(repository, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Fail(ResolvedRepository repository, string error)
    {
        _status.RecordFailure(repository.Key, error);
        _logger.LogWarning("Polling {RepositoryKey} failed; keeping the cached pull requests. {Error}", repository.Key, error);
    }

    /// <summary>Waits for the interval or an earlier request and says which ended the wait.</summary>
    private async Task<PollWake> WaitForNextPollAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            return await _trigger.WaitAsync(interval, cancellationToken) ? PollWake.Requested : PollWake.Scheduled;
        }
        catch (OperationCanceledException)
        {
            return PollWake.Stopping;
        }
    }

    private enum PollWake
    {
        /// <summary>The interval elapsed.</summary>
        Scheduled,

        /// <summary>A poll was asked for, or it is the first poll; either runs even while paused.</summary>
        Requested,

        /// <summary>The host is stopping.</summary>
        Stopping,
    }
}
