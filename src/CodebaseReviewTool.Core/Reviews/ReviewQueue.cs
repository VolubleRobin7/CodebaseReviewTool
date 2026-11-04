using System.Threading.Channels;

namespace CodebaseReviewTool.Core.Reviews;

/// <summary>
/// Ids of reviews waiting to run, in the order they were queued. Unbounded because a review id is tiny and the
/// number of reviews a person queues is small. The queue lives in memory only; the review rows are the record.
/// </summary>
public sealed class ReviewQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();

    /// <summary>Where the review runner reads queued ids from.</summary>
    public ChannelReader<int> Reader => _channel.Reader;

    public void Enqueue(int reviewId)
    {
        if (!_channel.Writer.TryWrite(reviewId))
        {
            // Only possible once the writer is completed, which this class never does.
            throw new InvalidOperationException($"Review {reviewId} could not be queued.");
        }
    }
}
