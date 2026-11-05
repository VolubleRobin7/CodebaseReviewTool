using System.Text.RegularExpressions;
using CodebaseReviewTool.Core.Agents;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Data.Entities;
using CodebaseReviewTool.Core.Data.Enums;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Reviews;

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
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    ReviewEvents events,
    IRepositoryRegistry registry,
    WorktreeService worktreeService,
    IAgentRunner agentRunner,
    IOptions<ClaudeOptions> agentOptions,
    ILogger<ReviewOrchestrator> logger)
{
    private const int DiagramStepIndex = 1;

    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly ReviewEvents _events = events;
    private readonly IRepositoryRegistry _registry = registry;
    private readonly WorktreeService _worktreeService = worktreeService;
    private readonly IAgentRunner _agentRunner = agentRunner;
    private readonly ClaudeOptions _agentOptions = agentOptions.Value;
    private readonly ILogger<ReviewOrchestrator> _logger = logger;

    /// <summary>
    /// Runs every step of the review that has not yet succeeded. Step failures are recorded on the step run and the
    /// review; other exceptions propagate for the caller to record. Cancellation stops the running agent and leaves the
    /// review unfinished.
    /// </summary>
    public async Task RunAsync(int reviewId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.Reviews
            .Include(r => r.StepRuns)
            .FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken)
            ?? throw new InvalidOperationException($"Review {reviewId} no longer exists.");

        var steps = await db.FlowSteps.AsNoTracking().OrderBy(s => s.Step).ToListAsync(cancellationToken);
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

    private async Task<PromptContext> BuildContextAsync(ReviewDbContext db, Review review, CancellationToken cancellationToken)
    {
        var pullRequest = await db.PullRequests.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == review.PullRequestId && p.RepositoryKey == review.RepositoryKey, cancellationToken)
            ?? throw new InvalidOperationException($"Pull request #{review.PullRequestId} is no longer in the cache.");

        var repositoryName = _registry.Find(review.RepositoryKey)?.DisplayName ?? review.RepositoryKey;

        var changedFiles = await _worktreeService.GetChangedFilesAsync(
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
        ReviewDbContext db,
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

        var request = new AgentRequest(
            Prompt: prompt,
            WorkingDirectory: review.WorktreePath,
            Model: step.Model ?? _agentOptions.DefaultModel,
            Timeout: TimeSpan.FromMinutes(step.TimeoutMinutes ?? _agentOptions.TimeoutMinutes),
            MaxTurns: _agentOptions.MaxTurns,
            AllowedTools: _agentOptions.AllowedTools,
            JsonSchema: isLast ? IssuesSchema.Schema : null);

        AgentResult result;
        try
        {
            result = await _agentRunner.RunAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The agent could not be started at all (missing executable, bad working directory).
            run.Error = $"{ex.GetType().Name}: {ex.Message}";
            return await FailAsync(db, review, run, number, cancellationToken);
        }

        run.CommandLine = result.CommandLine;
        run.RawOutputJson = result.RawOutput;
        run.ResultText = index == DiagramStepIndex ? StripFence(result.ResultText) : result.ResultText;
        run.StdErr = result.StandardError;
        run.ExitCode = result.ExitCode;
        run.AgentSessionId = result.SessionId;
        run.DurationMs = result.DurationMs;
        run.CostUsd = result.CostUsd;

        if (!result.Succeeded)
        {
            run.Error = result.Error ?? "The agent run failed.";
            return await FailAsync(db, review, run, number, cancellationToken);
        }

        if (isLast)
        {
            var parsed = IssuesParser.Parse(result.StructuredOutputJson, result.ResultText);
            if (!parsed.Succeeded)
            {
                run.Error = $"The issues could not be read from the output: {parsed.Error}";
                return await FailAsync(db, review, run, number, cancellationToken);
            }

            await db.Issues.Where(i => i.ReviewId == review.Id).ExecuteDeleteAsync(cancellationToken);
            foreach (var issue in parsed.Issues)
            {
                issue.ReviewId = review.Id;
                db.Issues.Add(issue);
            }

            _logger.LogInformation("Review {ReviewId}: parsed {Count} issues", review.Id, parsed.Issues.Count);
        }

        run.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Review {ReviewId}: step {Number} of {Count} succeeded", review.Id, number, stepCount);
        Publish(review);
        return true;
    }

    private async Task<bool> FailAsync(ReviewDbContext db, Review review, ReviewStepRun run, int number, CancellationToken cancellationToken)
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
