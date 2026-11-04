using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>Identifies the review that changed.</summary>
/// <param name="ReviewId">The review's database id.</param>
/// <param name="PullRequestId">The DevOps id of the reviewed pull request.</param>
/// <param name="RepositoryKey">The repository the pull request belongs to.</param>
public sealed record ReviewChangedEventArgs(int ReviewId, int PullRequestId, string RepositoryKey);

/// <summary>
/// In-process notifications about reviews and the pull request cache. Handlers run synchronously on the publisher's
/// thread (a background service or another circuit), so a Blazor component must marshal with <c>InvokeAsync</c>
/// and must unsubscribe when disposed. A throwing handler is logged and does not stop the others.
/// </summary>
public sealed class ReviewEvents(ILogger<ReviewEvents> logger)
{
    private readonly ILogger<ReviewEvents> _logger = logger;

    /// <summary>A review was created or its state changed.</summary>
    public event Action<ReviewChangedEventArgs>? ReviewChanged;

    /// <summary>The cached pull requests changed (added, updated or closed).</summary>
    public event Action? PullRequestsChanged;

    public void PublishReviewChanged(ReviewChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var handler in ReviewChanged?.GetInvocationList().Cast<Action<ReviewChangedEventArgs>>() ?? [])
        {
            Invoke(() => handler(args), nameof(ReviewChanged));
        }
    }

    public void PublishPullRequestsChanged()
    {
        foreach (var handler in PullRequestsChanged?.GetInvocationList().Cast<Action>() ?? [])
        {
            Invoke(handler, nameof(PullRequestsChanged));
        }
    }

    private void Invoke(Action handler, string eventName)
    {
        try
        {
            handler();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A {Event} handler threw", eventName);
        }
    }
}
