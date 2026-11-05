using System.Text.RegularExpressions;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;

namespace CodebaseReviewTool.Core.Reporting;

/// <summary>One review of the same pull request, for the report's history list.</summary>
/// <param name="ReviewId">The review's database id.</param>
/// <param name="QueuedAt">When the review was queued (UTC).</param>
/// <param name="CompletedAt">When it finished or failed (UTC); null while it is in flight.</param>
/// <param name="State">The review's derived state.</param>
/// <param name="IssueCount">Issues the review found.</param>
public sealed record ReviewHistoryEntry(int ReviewId, DateTime QueuedAt, DateTime? CompletedAt, ReviewState State, int IssueCount);

/// <summary>
/// Everything the report page and the downloaded report show for one review. What a step's output means is decided by
/// its position in the flow: the first step is the summary, the second the diagram and the last the issues.
/// </summary>
public sealed partial class ReportData
{
    /// <summary>Position in the flow of the step whose text is the summary.</summary>
    public const int SummaryIndex = 0;

    /// <summary>Position in the flow of the step whose text is the diagram HTML.</summary>
    public const int DiagramIndex = 1;

    /// <param name="review">The review, with <see cref="Review.StepRuns"/> and <see cref="Review.Issues"/> loaded.</param>
    /// <param name="pullRequest">The reviewed pull request.</param>
    /// <param name="repositoryName">The repository's display name.</param>
    /// <param name="flowSteps">The flow's steps in run order.</param>
    /// <param name="history">Every review of the same pull request, newest first, this one included.</param>
    public ReportData(
        Review review,
        PullRequest pullRequest,
        string repositoryName,
        IReadOnlyList<FlowStep> flowSteps,
        IReadOnlyList<ReviewHistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(pullRequest);

        Review = review;
        PullRequest = pullRequest;
        RepositoryName = repositoryName;
        FlowSteps = flowSteps;
        History = history;

        Issues = [.. review.Issues
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Line ?? 0)
            .ThenBy(i => i.Id)];
    }

    public Review Review { get; }

    public PullRequest PullRequest { get; }

    public string RepositoryName { get; }

    /// <summary>The flow's steps in run order, as they are now; the section headings come from their names.</summary>
    public IReadOnlyList<FlowStep> FlowSteps { get; }

    /// <summary>Every review of the same pull request, newest first, this one included.</summary>
    public IReadOnlyList<ReviewHistoryEntry> History { get; }

    /// <summary>The review's issues, most severe first, then by file and line.</summary>
    public IReadOnlyList<Issue> Issues { get; }

    public ReviewState State => Review.State;

    public bool IsInProgress => State is ReviewState.Queued or ReviewState.PreparingWorktree or ReviewState.Running;

    /// <summary>When the review finished, as the report's "Reviewed" line shows it.</summary>
    public string ReviewedText => Review switch
    {
        { CompletedAt: { } completed } when State == ReviewState.Failed => $"{ReportFormat.Timestamp(completed)} (failed)",
        { CompletedAt: { } completed } => ReportFormat.Timestamp(completed),
        _ when State == ReviewState.Queued => "Queued",
        _ => "In progress",
    };

    /// <summary>The reviewed commit, abbreviated.</summary>
    public string ShortCommit => Review.SourceCommit.Length > 10 ? Review.SourceCommit[..10] : Review.SourceCommit;

    /// <summary>The summary step's text split into paragraphs at blank lines; empty until that step has succeeded.</summary>
    public IReadOnlyList<string> SummaryParagraphs => SucceededRun(SummaryIndex) is { } run
        ? SplitParagraphs(run.ResultText)
        : [];

    /// <summary>The diagram step's HTML excerpt, or null until that step has succeeded.</summary>
    public string? DiagramHtml => SucceededRun(DiagramIndex)?.ResultText;

    /// <summary>The 1-based position of the step in flight, or null when no step is running.</summary>
    public int? CurrentStepNumber => Review.CurrentStep is { } step && IndexOf(step) is var index && index >= 0
        ? index + 1
        : null;

    /// <summary>
    /// The position of the first step that has not succeeded, which is where a re-run of a review that failed outside
    /// any step (preparing its worktree, say) starts. Null when every step succeeded.
    /// </summary>
    public int? FirstUnfinishedIndex
    {
        get
        {
            for (var index = 0; index < FlowSteps.Count; index++)
            {
                if (SucceededRun(index) is null)
                {
                    return index;
                }
            }

            return null;
        }
    }

    /// <summary>True when the review failed without any of its step runs failing, e.g. while preparing its worktree.</summary>
    public bool FailedOutsideSteps => State == ReviewState.Failed && Review.StepRuns.All(r => r.State != StepRunState.Failed);

    /// <summary>The heading of the section produced by the step at <paramref name="index"/>: the step's name.</summary>
    public string SectionTitle(int index) => index >= 0 && index < FlowSteps.Count && !string.IsNullOrWhiteSpace(FlowSteps[index].Name)
        ? FlowSteps[index].Name
        : $"Step {index + 1}";

    /// <summary>The run of the step at <paramref name="index"/> (0-based position in the flow), or null if it has not run.</summary>
    public ReviewStepRun? RunAt(int index) => index >= 0 && index < FlowSteps.Count
        ? Review.StepRuns.FirstOrDefault(r => r.Step == FlowSteps[index].Step)
        : null;

    public int CountOf(IssueSeverity severity) => Issues.Count(i => i.Severity == severity);

    private ReviewStepRun? SucceededRun(int index) => RunAt(index) is { State: StepRunState.Succeeded } run ? run : null;

    private int IndexOf(int step)
    {
        for (var index = 0; index < FlowSteps.Count; index++)
        {
            if (FlowSteps[index].Step == step)
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] SplitParagraphs(string text) =>
    [
        .. BlankLine().Split(text.ReplaceLineEndings("\n"))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0),
    ];

    // A blank line, which may hold stray spaces or tabs, separates paragraphs.
    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLine();
}
