using CodebaseReviewTool.Domain.Entities;

namespace CodebaseReviewTool.Application.Common.Extensions;

internal static class ReviewQueryExtensions
{
    /// <summary>
    /// Runs the query and returns each review with its <see cref="Review.StepRuns"/> loaded, which
    /// <see cref="Review.State"/> and <see cref="Review.CurrentStep"/> need.
    /// </summary>
    public static List<Review> ToListWithStepRuns(this IQueryable<Review> reviews) =>
    [
        .. reviews
            .Select(review => new { Review = review, review.StepRuns })
            .AsEnumerable()
            .Select(row =>
            {
                row.Review.StepRuns = row.StepRuns;
                return row.Review;
            }),
    ];

    /// <summary>The first review the query returns, with its step runs loaded; null when there is none.</summary>
    public static Review? FirstOrDefaultWithStepRuns(this IQueryable<Review> reviews) =>
        reviews.Take(1).ToListWithStepRuns().FirstOrDefault();
}
