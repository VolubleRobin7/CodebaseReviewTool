using System.Text;
using CodebaseReviewTool.Core.Reporting;

namespace CodebaseReviewTool.Host.Api;

public static class ReportEndpoints
{
    /// <summary>
    /// Maps <c>GET /api/reviews/{id}/report</c>, which returns the review's report as a standalone HTML file to save
    /// (<c>Content-Disposition: attachment</c>), or 404 when there is no such review.
    /// </summary>
    public static IEndpointRouteBuilder MapReportApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/reviews/{id:int}/report", GetReportAsync).WithName("GetReviewReport");

        return app;
    }

    private static async Task<IResult> GetReportAsync(int id, ReportDataLoader loader, CancellationToken cancellationToken)
    {
        var report = await loader.LoadAsync(id, cancellationToken);
        if (report is null)
        {
            return Results.NotFound();
        }

        var html = Encoding.UTF8.GetBytes(ReportBuilder.Build(report));
        return Results.File(html, "text/html; charset=utf-8", $"review-{id}-pr-{report.PullRequest.Id}.html");
    }
}
