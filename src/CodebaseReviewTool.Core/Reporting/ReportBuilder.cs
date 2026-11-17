using System.Net;
using System.Text;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Reporting;

/// <summary>
/// Renders a review's report as one standalone HTML file: the same metadata, summary, diagram and issues as the report
/// page, with the styles inline and the diagram inlined into its sandboxed iframe's <c>srcdoc</c>. Nothing is loaded
/// from anywhere else, apart from whatever the diagram excerpt loads inside its own frame.
/// </summary>
public static class ReportBuilder
{
    /// <summary>Returns the complete HTML document for the review's report.</summary>
    public static string Build(ReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var pullRequest = report.PullRequest;
        var html = new StringBuilder();

        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>#").Append(pullRequest.Id).Append(' ').Append(Encode(pullRequest.Title)).Append(" - Review report</title>\n")
            .Append("<style>\n").Append(ReportTemplates.ReportStyles).Append("\n</style>\n</head>\n<body>\n<main class=\"page\">\n");

        html.Append("<div class=\"report-top\">\n");
        AppendAside(html, report);
        html.Append("<div class=\"report-main\">\n");
        AppendHead(html, report);
        AppendSummary(html, report);
        html.Append("</div>\n</div>\n");

        AppendDiagram(html, report);
        AppendIssues(html, report);

        html.Append("<p class=\"report-foot\">Review ").Append(report.Review.Id)
            .Append(" of pull request #").Append(pullRequest.Id)
            .Append(". Saved from Codebase Review Tool on ").Append(Encode(ReportFormat.Timestamp(DateTime.UtcNow))).Append(".</p>\n");

        html.Append("</main>\n<script>\n")
            .Append(ReportTemplates.ReportScript)
            .Append("\n</script>\n</body>\n</html>\n");

        return html.ToString();
    }

    private static void AppendAside(StringBuilder html, ReportData report)
    {
        var pullRequest = report.PullRequest;

        html.Append("<aside class=\"report-aside\">\n<dl class=\"kv-stack\">\n");
        AppendTerm(html, "Repository", Encode(report.RepositoryName));
        AppendTerm(html, "Author", Encode(pullRequest.AuthorName));
        AppendTerm(html, "Branch", $"<span class=\"mono\">{Encode(pullRequest.SourceBranch)}</span>");
        AppendTerm(html, "Commit", $"<span class=\"mono\">{Encode(report.ShortCommit)}</span>");
        AppendTerm(html, "Reviewed", Encode(report.ReviewedText));
        if (report.TotalDurationMs is { } duration)
        {
            AppendTerm(html, "Duration", Encode(ReportFormat.Duration(duration)));
        }

        if (report.TotalCostUsd is { } cost)
        {
            var hint = report.StepsWithUnknownCost > 0
                ? $" <span class=\"hint\" role=\"img\" title=\"{Encode(ReportFormat.UnknownCostNote(report.StepsWithUnknownCost))}\">?</span>"
                : string.Empty;
            AppendTerm(html, "Cost", Encode(ReportFormat.Cost(cost)) + hint);
        }

        html.Append("</dl>\n<div class=\"report-counts\">\n");

        foreach (var severity in ReportFormat.Severities)
        {
            html.Append("<div class=\"report-count\"><span class=\"report-count-label\"><span class=\"report-dot ").Append(ReportFormat.SeverityClass(severity))
                .Append("\"></span>").Append(severity).Append("</span><span class=\"report-count-value\">")
                .Append(report.CountOf(severity)).Append("</span></div>\n");
        }

        html.Append("</div>\n</aside>\n");
    }

    private static void AppendHead(StringBuilder html, ReportData report)
    {
        var pullRequest = report.PullRequest;

        html.Append("<header class=\"report-head\">\n<span class=\"report-number\">#").Append(pullRequest.Id).Append("</span>\n")
            .Append("<h1 class=\"page-title\">").Append(Encode(pullRequest.Title))
            .Append(pullRequest.IsDraft ? " <span class=\"tag\">Draft</span>" : string.Empty)
            .Append(pullRequest.MergeStatus == "conflicts" ? " <span class=\"tag tag-high\">Conflicts</span>" : string.Empty)
            .Append("</h1>\n");

        html.Append("<p class=\"report-links\">");
        if (Uri.TryCreate(pullRequest.Url, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
        {
            html.Append("<a href=\"").Append(Encode(pullRequest.Url)).Append("\" rel=\"noopener\" target=\"_blank\">Open in DevOps</a>");
        }

        html.Append("<span>").Append(Encode(pullRequest.SourceBranch)).Append(" into ").Append(Encode(pullRequest.TargetBranch)).Append("</span></p>\n");

        if (report.State == ReviewState.Failed && report.Review.Error is { } error)
        {
            html.Append("<p class=\"report-error\">").Append(Encode(error)).Append("</p>\n");
        }

        if (report.State == ReviewState.Cancelled)
        {
            html.Append("<p class=\"report-note\">").Append(Encode(ReportFormat.CancelledNote)).Append("</p>\n");
        }

        html.Append("</header>\n");
    }

    private static void AppendSummary(StringBuilder html, ReportData report)
    {
        AppendSectionHead(html, report.SectionTitle(ReportData.SummaryIndex));

        var paragraphs = report.SummaryParagraphs;
        if (paragraphs.Count > 0)
        {
            html.Append("<div class=\"report-summary\">\n");
            foreach (var paragraph in paragraphs)
            {
                html.Append("<p>").Append(Encode(paragraph)).Append("</p>\n");
            }

            html.Append("</div>\n");
        }
        else
        {
            AppendUnavailable(html, report, ReportData.SummaryIndex, "The summary is empty.");
        }

        html.Append("</section>\n");
    }

    private static void AppendDiagram(StringBuilder html, ReportData report)
    {
        AppendSectionHead(html, report.SectionTitle(ReportData.DiagramIndex));

        if (report.DiagramHtml is { } diagram && !string.IsNullOrWhiteSpace(diagram))
        {
            html.Append("<iframe class=\"diagram-frame\" sandbox=\"allow-scripts\" title=\"Architecture diagram\" srcdoc=\"")
                .Append(Encode(DiagramDocument.Wrap(diagram)))
                .Append("\"></iframe>\n");
        }
        else
        {
            AppendUnavailable(html, report, ReportData.DiagramIndex, "The diagram is empty.");
        }

        html.Append("</section>\n");
    }

    private static void AppendIssues(StringBuilder html, ReportData report)
    {
        var index = report.FlowSteps.Count - 1;
        var issues = report.Issues;

        html.Append("<section class=\"section\">\n<div class=\"section-head\">\n<h2 class=\"section-title\">")
            .Append(Encode(report.SectionTitle(index))).Append("</h2>\n");

        if (issues.Count > 0)
        {
            html.Append("<div class=\"pills\">");
            html.Append("<button class=\"pill active\" data-severity=\"All\" type=\"button\">All ").Append(issues.Count).Append("</button>");
            foreach (var severity in ReportFormat.Severities)
            {
                html.Append("<button class=\"pill\" data-severity=\"").Append(severity).Append("\" type=\"button\">")
                    .Append(severity).Append(' ').Append(report.CountOf(severity)).Append("</button>");
            }

            html.Append("</div>\n");
        }

        html.Append("</div>\n");

        if (report.RunAt(index) is not { State: StepRunState.Succeeded })
        {
            AppendUnavailable(html, report, index, string.Empty);
        }
        else if (issues.Count == 0)
        {
            html.Append("<p class=\"empty\">No issues found.</p>\n");
        }
        else
        {
            html.Append("<div class=\"rows\">\n<div class=\"row-head\"><span>Severity</span><span>Description</span><span>Suggested fix</span></div>\n");
            foreach (var issue in issues)
            {
                AppendIssue(html, issue);
            }

            html.Append("</div>\n<p class=\"empty\" hidden id=\"issues-filter-empty\">No issues at this severity.</p>\n");
        }

        html.Append("</section>\n");
    }

    private static void AppendIssue(StringBuilder html, Issue issue)
    {
        html.Append("<div class=\"row issue-row\" data-severity=\"").Append(issue.Severity).Append("\">\n")
            .Append("<div class=\"issue-severity\"><span class=\"sev ").Append(ReportFormat.SeverityClass(issue.Severity)).Append("\">")
            .Append(issue.Severity).Append("</span>");

        if (!string.IsNullOrWhiteSpace(issue.Category))
        {
            html.Append("<span class=\"issue-category\">").Append(Encode(issue.Category)).Append("</span>");
        }

        html.Append("</div>\n<div class=\"issue-text\">");
        if (!string.IsNullOrWhiteSpace(issue.Title))
        {
            html.Append("<span class=\"issue-title\">").Append(Encode(issue.Title)).Append("</span>");
        }

        html.Append("<span class=\"issue-description\">").Append(Encode(issue.Description)).Append("</span>");
        if (!string.IsNullOrWhiteSpace(issue.File))
        {
            html.Append("<span class=\"issue-location\">").Append(Encode(ReportFormat.Location(issue))).Append("</span>");
        }

        html.Append("</div>\n<span class=\"issue-fix\">").Append(Encode(issue.Suggestion ?? "None suggested.")).Append("</span>\n</div>\n");
    }

    private static void AppendSectionHead(StringBuilder html, string title) =>
        html.Append("<section class=\"section\">\n<div class=\"section-head\"><h2 class=\"section-title\">")
            .Append(Encode(title)).Append("</h2></div>\n");

    /// <summary>Explains why a section has no content: its step failed, has not run, or produced nothing.</summary>
    private static void AppendUnavailable(StringBuilder html, ReportData report, int index, string emptyText)
    {
        var run = report.RunAt(index);
        var text = run switch
        {
            { State: StepRunState.Failed } => $"This step failed: {run.Error}",
            { State: StepRunState.Succeeded } => emptyText,
            { State: StepRunState.Cancelled } => "This step was cancelled before it finished.",
            _ when report.State == ReviewState.Failed => "This step did not run because the review stopped earlier.",
            _ when report.State == ReviewState.Cancelled => "This step did not run because the review was cancelled.",
            _ => "This step has not finished yet.",
        };

        html.Append("<p class=\"").Append(run is { State: StepRunState.Failed } ? "report-error" : "empty").Append("\">")
            .Append(Encode(text)).Append("</p>\n");
    }

    private static void AppendTerm(StringBuilder html, string term, string encodedValue) =>
        html.Append("<div><dt>").Append(term).Append("</dt><dd>").Append(encodedValue).Append("</dd></div>\n");

    private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
