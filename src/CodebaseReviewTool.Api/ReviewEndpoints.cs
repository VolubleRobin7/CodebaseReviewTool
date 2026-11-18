using System.Globalization;
using System.Net;
using CodebaseReviewTool.Application.PullRequests;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Application.Reviews;
using CodebaseReviewTool.Domain.Enums;
using CodebaseReviewTool.Git;
using CodebaseReviewTool.RepositoryHosts.AzureDevOps;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Api;

/// <summary>Body of <c>POST /api/reviews</c>: either the pull request's page URL, or its id (and repository).</summary>
/// <param name="PullRequestUrl">The pull request page, e.g. <c>https://dev.azure.com/org/project/_git/repo/pullrequest/123</c>.</param>
/// <param name="PullRequestId">The DevOps pull request id, when no URL is given.</param>
/// <param name="RepositoryKey">The configured repository's key; may be left out when only one repository is configured.</param>
public sealed record StartReviewRequest(string? PullRequestUrl, int? PullRequestId, string? RepositoryKey);

/// <summary>A review's status as the API returns it. Times are UTC.</summary>
public sealed record ReviewStatusResponse(
    int Id,
    string State,
    bool IsFinished,
    int? CurrentStep,
    ReviewPullRequestResponse PullRequest,
    string SourceCommit,
    bool IsOutOfDate,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? Error,
    IReadOnlyList<ReviewStepResponse> Steps,
    ReviewIssuesResponse Issues,
    ReviewLinksResponse Links);

public sealed record ReviewPullRequestResponse(int Id, string RepositoryKey, string Title, string Url);

public sealed record ReviewStepResponse(int Step, string State, DateTime? StartedAt, DateTime? CompletedAt, long? DurationMs);

/// <summary>Issue counts; <paramref name="BySeverity"/> has every severity, zero included.</summary>
public sealed record ReviewIssuesResponse(int Total, IReadOnlyDictionary<string, int> BySeverity);

/// <param name="Report">The report page in the UI, for a person to open.</param>
/// <param name="Download">The report as a standalone HTML file.</param>
/// <param name="Status">This status resource, to poll until the review finishes.</param>
public sealed record ReviewLinksResponse(string Report, string Download, string Status);

public static class ReviewEndpoints
{
    // Suggested by Retry-After while a review is unfinished; reviews take minutes, so polling faster gains little.
    private const int RetryAfterSeconds = 30;

    /// <summary>
    /// Maps the review API:
    /// <list type="bullet">
    /// <item><c>POST /api/reviews</c> starts a review of a pull request and returns its status at once: 202 with a
    /// <c>Location</c> header for a new review, or 200 with the review already queued or running for that pull
    /// request.</item>
    /// <item><c>GET /api/reviews/{id}</c> returns a review's status, links to its report and, while it is
    /// unfinished, a <c>Retry-After</c> header saying when to ask again.</item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapReviewApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/reviews", StartReviewAsync).WithName("StartReview");
        app.MapGet("/api/reviews/{id:int}", GetReviewAsync).WithName("GetReview");

        return app;
    }

    private static async Task<IResult> StartReviewAsync(
        StartReviewRequest request,
        HttpContext context,
        IRepositoryRegistry registry,
        IOptions<DevOpsOptions> devOpsOptions,
        IDevOpsClient devOps,
        PullRequestCache cache,
        ReviewService reviews,
        ReviewStatusReader statusReader,
        CancellationToken cancellationToken)
    {
        var target = ResolveTarget(request, registry, devOpsOptions.Value.OrganizationUrl);
        if (target is not { Repository: { } repository, PullRequestId: var pullRequestId, Error: null })
        {
            return Problem(HttpStatusCode.BadRequest, "Invalid request", target.Error);
        }

        // Fetch rather than trust the cache: the caller has often just pushed, and the poller may not have seen the
        // new commit (or the new pull request) yet.
        var fetched = await devOps.GetPullRequestAsync(repository, pullRequestId, cancellationToken);
        if (!fetched.Succeeded)
        {
            return fetched.StatusCode == HttpStatusCode.NotFound
                ? Problem(HttpStatusCode.NotFound, "Pull request not found", $"Pull request #{pullRequestId} does not exist in '{repository.Key}'.")
                : Problem(HttpStatusCode.BadGateway, "Azure DevOps request failed", fetched.Error);
        }

        var pullRequest = fetched.Value!;
        if (!PullRequestCache.IsActive(pullRequest))
        {
            return Problem(HttpStatusCode.Conflict, "Pull request is not active", $"Pull request #{pullRequestId} is {pullRequest.Status}.");
        }

        await cache.UpsertAsync(repository, pullRequest, cancellationToken);

        int reviewId;
        bool created;
        try
        {
            reviewId = (await reviews.StartReviewAsync(pullRequestId, repository.Key, cancellationToken)).Id;
            created = true;
        }
        catch (ReviewAlreadyActiveException ex)
        {
            reviewId = ex.ReviewId;
            created = false;
        }

        var status = await statusReader.GetStatusAsync(reviewId, cancellationToken);
        if (status is null)
        {
            // Only if the review was discarded between starting it and reading it back.
            return Problem(HttpStatusCode.Conflict, "Review no longer exists", $"Review {reviewId} was removed before it could be read.");
        }

        var body = ToResponse(status, context.Request);
        SetRetryAfter(context, status);
        return created ? TypedResults.Accepted(body.Links.Status, body) : TypedResults.Ok(body);
    }

    private static async Task<IResult> GetReviewAsync(
        int id,
        HttpContext context,
        ReviewStatusReader statusReader,
        CancellationToken cancellationToken)
    {
        var status = await statusReader.GetStatusAsync(id, cancellationToken);
        if (status is null)
        {
            return Problem(HttpStatusCode.NotFound, "Review not found", $"There is no review {id}.");
        }

        SetRetryAfter(context, status);
        return TypedResults.Ok(ToResponse(status, context.Request));
    }

    private sealed record Target(ResolvedRepository? Repository, int PullRequestId, string? Error);

    private static Target ResolveTarget(StartReviewRequest request, IRepositoryRegistry registry, string organizationUrl)
    {
        if (!string.IsNullOrWhiteSpace(request.PullRequestUrl))
        {
            if (!AzureDevOpsUrl.TryParsePullRequestUrl(request.PullRequestUrl, organizationUrl, out var parts, out var idFromUrl))
            {
                return Invalid("pullRequestUrl is not an Azure DevOps pull request URL (.../_git/<repo>/pullrequest/<id>).");
            }

            if (request.PullRequestId is { } givenId && givenId != idFromUrl)
            {
                return Invalid($"pullRequestId {givenId} does not match pull request #{idFromUrl} in pullRequestUrl.");
            }

            var match = registry.Repositories.FirstOrDefault(r =>
                r.Organization.Equals(parts.Organization, StringComparison.OrdinalIgnoreCase)
                && r.Project.Equals(parts.Project, StringComparison.OrdinalIgnoreCase)
                && r.Repository.Equals(parts.Repository, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return Invalid($"Repository '{parts.Project}/{parts.Repository}' is not configured. Configured: {DescribeKeys(registry)}.");
            }

            if (!string.IsNullOrWhiteSpace(request.RepositoryKey) && !match.Key.Equals(request.RepositoryKey, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid($"repositoryKey '{request.RepositoryKey}' does not match '{match.Key}' from pullRequestUrl.");
            }

            return new Target(match, idFromUrl, null);
        }

        if (request.PullRequestId is not { } pullRequestId || pullRequestId <= 0)
        {
            return Invalid("Give either pullRequestUrl, or pullRequestId (and repositoryKey when more than one repository is configured).");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryKey))
        {
            return registry.Repositories.Count == 1
                ? new Target(registry.Repositories[0], pullRequestId, null)
                : Invalid($"repositoryKey is required when more than one repository is configured. Configured: {DescribeKeys(registry)}.");
        }

        return registry.Find(request.RepositoryKey) is { } repository
            ? new Target(repository, pullRequestId, null)
            : Invalid($"Repository '{request.RepositoryKey}' is not configured. Configured: {DescribeKeys(registry)}.");

        static Target Invalid(string error) => new(null, 0, error);
    }

    private static string DescribeKeys(IRepositoryRegistry registry) =>
        registry.Repositories.Count == 0 ? "none" : string.Join(", ", registry.Repositories.Select(r => r.Key));

    private static ReviewStatusResponse ToResponse(ReviewStatus status, HttpRequest request)
    {
        var review = status.Review;
        var pullRequest = review.PullRequest;
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";

        var bySeverity = Enum.GetValues<IssueSeverity>().ToDictionary(Camel, s => status.IssueCounts.GetValueOrDefault(s));

        return new ReviewStatusResponse(
            review.Id,
            Camel(status.State),
            status.IsFinished,
            review.CurrentStep,
            new ReviewPullRequestResponse(review.PullRequestId, review.RepositoryKey, pullRequest?.Title ?? string.Empty, pullRequest?.Url ?? string.Empty),
            review.SourceCommit,
            pullRequest is not null && review.IsOutOfDate(pullRequest),
            review.QueuedAt,
            review.StartedAt,
            review.CompletedAt,
            review.CancelledAt,
            review.Error,
            [.. status.Steps.Select(s => new ReviewStepResponse(s.Step, Camel(s.State), s.StartedAt, s.CompletedAt, s.DurationMs))],
            new ReviewIssuesResponse(bySeverity.Values.Sum(), bySeverity),
            new ReviewLinksResponse(
                $"{baseUrl}/reviews/{review.Id}",
                $"{baseUrl}/api/reviews/{review.Id}/report",
                $"{baseUrl}/api/reviews/{review.Id}"));
    }

    private static void SetRetryAfter(HttpContext context, ReviewStatus status)
    {
        if (!status.IsFinished)
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Enum names as JSON-style camelCase (PreparingWorktree -> preparingWorktree).
    private static string Camel<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return string.Concat(char.ToLowerInvariant(name[0]).ToString(), name[1..]);
    }

    private static IResult Problem(HttpStatusCode status, string title, string? detail) =>
        TypedResults.Problem(detail: detail, statusCode: (int)status, title: title);
}
