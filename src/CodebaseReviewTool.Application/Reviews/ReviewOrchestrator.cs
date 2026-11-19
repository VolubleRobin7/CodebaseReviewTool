using System.Text.RegularExpressions;
using CodebaseReviewTool.Application.Agents;
using CodebaseReviewTool.Application.Common.Extensions;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using CodebaseReviewTool.Application.Configuration;
using CodebaseReviewTool.Application.Prompts;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Domain.Entities;
using CodebaseReviewTool.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Application.Reviews;

/// <summary>
/// Runs the flow steps of a review whose worktree is ready. Steps run in ascending order, each as an independent agent
/// run in the worktree, with one <see cref="ReviewStepRun"/> row per step. A step that already succeeded is skipped,
/// which is how a review continues after a re-run from a later step. The first failure stops the review.
/// </summary>
/// <remarks>
/// What a step's output means is decided by its position: the second step's text is the diagram HTML, and the last
/// step gets the issues schema and its output is parsed into <see cref="Issue"/> rows.
/// </remarks>
public sealed partial class ReviewOrchestrator(
    IReviewDbContextFactory dbContextFactory,
    ReviewEvents events,
    IRepositoryRegistry registry,
    ISourceControl sourceControl,
    IAgentRegistry agents,
    IOptions<ReviewOptions> reviewOptions,
    ILogger<ReviewOrchestrator> logger)
{
    private const int DiagramStepIndex = 1;

    private readonly IReviewDbContextFactory _dbContextFactory = dbContextFactory;
    private readonly ReviewEvents _events = events;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly ISourceControl _sourceControl = sourceControl;
    private readonly IAgentRegistry _agents = agents;
    private readonly ReviewOptions _reviewOptions = reviewOptions.Value;
    private readonly ILogger<ReviewOrchestrator> _logger = logger;

    /// <summary>
    /// Runs every step of the review that has not yet succeeded. Step failures are recorded on the step run and the
    /// review; other exceptions propagate for the caller to record. Cancellation stops the running agent and leaves the
    /// review unfinished.
    /// </summary>
    public async Task RunAsync(int reviewId, CancellationToken cancellationToken)
    {
        await using var db = _dbContextFactory.CreateDbContext();

        var review = db.GetEntities<Review>()
            .Where(r => r.Id == reviewId)
            .FirstOrDefaultWithStepRuns()
            ?? throw new InvalidOperationException($"Review {reviewId} no longer exists.");

        var steps = db.GetEntities<FlowStep>().OrderBy(s => s.Step).ToList();
        if (steps.Count == 0)
        {
            throw new InvalidOperationException("The flow has no steps.");
        }

        var context = await BuildContextAsync(db, review, cancellationToken);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (review.StepRuns.Any(r => r.Step == step.Step && r.State == StepRunState.Succeeded))
            {
                continue;
            }

            var succeeded = await RunStepAsync(db, review, step, index, steps.Count, context, cancellationToken);
            if (!succeeded)
            {
                return;
            }
        }

        review.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Review {ReviewId} of pull request #{PullRequestId} completed", review.Id, review.PullRequestId);
        Publish(review);
    }

    private async Task<PromptContext> BuildContextAsync(IReviewDbContext db, Review review, CancellationToken cancellationToken)
    {
        var pullRequest = db.GetEntities<PullRequest>()
            .FirstOrDefault(p => p.Id == review.PullRequestId && p.RepositoryKey == review.RepositoryKey)
            ?? throw new InvalidOperationException($"Pull request #{review.PullRequestId} is no longer in the cache.");

        var repositoryName = _registry.Find(review.RepositoryKey)?.DisplayName ?? review.RepositoryKey;

        var changedFiles = await _sourceControl.GetChangedFilesAsync(
            review.WorktreePath, review.MergeBaseCommit, review.SourceCommit, cancellationToken);

        return new PromptContext(
            PullRequestId: pullRequest.Id,
            Title: pullRequest.Title,
            Description: pullRequest.Description,
            Author: pullRequest.AuthorName,
            Url: pullRequest.Url,
            RepositoryKey: review.RepositoryKey,
            RepositoryName: repositoryName,
            SourceBranch: pullRequest.SourceBranch,
            TargetBranch: pullRequest.TargetBranch,
            SourceCommit: review.SourceCommit,
            MergeBase: review.MergeBaseCommit,
            ChangedFiles: string.Join('\n', changedFiles.Select(f => $"{f.Status}\t{f.Path}")),
            WorktreePath: review.WorktreePath);
    }

    /// <summary>Runs one step and records its outcome; returns false when the step failed and the review stopped.</summary>
    private async Task<bool> RunStepAsync(
        IReviewDbContext db,
        Review review,
        FlowStep step,
        int index,
        int stepCount,
        PromptContext context,
        CancellationToken cancellationToken)
    {
        var isLast = index == stepCount - 1;
        var number = index + 1;

        var prompt = PromptRenderer.Render(step.PromptText, context).Text;
        if (isLast)
        {
            prompt += IssuesSchema.Instruction;
        }

        var run = new ReviewStepRun
        {
            ReviewId = review.Id,
            Step = step.Step,
            RenderedPrompt = prompt,
            StartedAt = DateTime.UtcNow,
        };
        review.StepRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Review {ReviewId}: step {Number} of {Count} started", review.Id, number, stepCount);
        Publish(review);

        // Every step runs on the default agent until steps can choose one.
        var agent = _agents.Default;

        // A step without its own model uses the agent's default (its first), and one without its own timeout uses
        // Reviews:StepTimeoutMinutes. No effort is sent until steps can store one, so each model runs at its own
        // default.
        var request = new PromptRequest(
            Prompt: prompt,
            WorkingDirectory: review.WorktreePath,
            Model: step.Model ?? agent.Models[0].Name,
            Timeout: TimeSpan.FromMinutes(step.TimeoutMinutes ?? _reviewOptions.StepTimeoutMinutes),
            JsonSchema: isLast ? IssuesSchema.Schema : null);

        PromptResult result;
        try
        {
            result = await agent.RunPromptAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The agent could not be started at all (missing executable, bad working directory).
            run.Error = $"{ex.GetType().Name}: {ex.Message}";
            return await FailAsync(db, review, run, number, cancellationToken);
        }

        // Until the step run table is reworked, the agent's diagnostics go in the old stderr column; the command line
        // and exit code columns are left empty because they are part of the diagnostics.
        run.RawOutputJson = result.RawOutput;
        run.ResultText = index == DiagramStepIndex ? StripFence(result.Text) : result.Text;
        run.StdErr = result.Diagnostics ?? string.Empty;
        run.AgentSessionId = result.ConversationId;
        run.DurationMs = (long)result.Duration.TotalMilliseconds;
        run.CostUsd = result.CostUsd;

        if (result.Error is { } error)
        {
            run.Error = error;
            return await FailAsync(db, review, run, number, cancellationToken);
        }

        if (isLast)
        {
            var parsed = IssuesParser.Parse(result.StructuredOutputJson, result.Text);
            if (!parsed.Succeeded)
            {
                run.Error = $"The issues could not be read from the output: {parsed.Error}";
                return await FailAsync(db, review, run, number, cancellationToken);
            }

            // Replaced in the same save that marks the step succeeded.
            foreach (var stale in db.GetEntities<Issue>().Where(i => i.ReviewId == review.Id).ToList())
            {
                db.Remove(stale);
            }

            foreach (var issue in parsed.Issues)
            {
                issue.ReviewId = review.Id;
                db.Add(issue);
            }

            _logger.LogInformation("Review {ReviewId}: parsed {Count} issues", review.Id, parsed.Issues.Count);
        }

        run.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Review {ReviewId}: step {Number} of {Count} succeeded", review.Id, number, stepCount);
        Publish(review);
        return true;
    }

    private async Task<bool> FailAsync(IReviewDbContext db, Review review, ReviewStepRun run, int number, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        run.CompletedAt = now;
        review.Error = $"Step {number} failed: {run.Error}";
        review.CompletedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Review {ReviewId}: {Error}", review.Id, review.Error);
        Publish(review);
        return false;
    }

    /// <summary>Removes a Markdown code fence (<c>```</c> or <c>```html</c>) wrapped around the whole text.</summary>
    internal static string StripFence(string text)
    {
        var match = SurroundingFence().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : text;
    }

    private void Publish(Review review) =>
        _events.PublishReviewChanged(new ReviewChangedEventArgs(review.Id, review.PullRequestId, review.RepositoryKey));

    [GeneratedRegex(@"^\s*```[A-Za-z]*[ \t]*\r?\n(.*?)\r?\n[ \t]*```\s*$", RegexOptions.Singleline)]
    private static partial Regex SurroundingFence();
}
