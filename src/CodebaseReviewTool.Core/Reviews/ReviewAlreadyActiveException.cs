namespace CodebaseReviewTool.Core.Reviews;

/// <summary>
/// Thrown by <see cref="ReviewService.StartReviewAsync"/> when the pull request already has a review that is queued,
/// preparing its worktree or running. Derives from <see cref="InvalidOperationException"/> so callers that only show
/// the message need not know about it; the API uses <see cref="ReviewId"/> to hand back the review in flight.
/// </summary>
public sealed class ReviewAlreadyActiveException(int reviewId, string message) : InvalidOperationException(message)
{
    /// <summary>The review already in flight.</summary>
    public int ReviewId { get; } = reviewId;
}
