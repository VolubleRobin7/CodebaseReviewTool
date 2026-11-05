namespace CodebaseReviewTool.Core.Prompts;

/// <summary>The values substituted into a step's prompt. Each property feeds one placeholder.</summary>
/// <param name="PullRequestId">The DevOps pull request id (<c>{{PR_ID}}</c>).</param>
/// <param name="Title">Pull request title (<c>{{PR_TITLE}}</c>).</param>
/// <param name="Description">Pull request description (<c>{{PR_DESCRIPTION}}</c>).</param>
/// <param name="Author">Author's display name (<c>{{PR_AUTHOR}}</c>).</param>
/// <param name="Url">Web link to the pull request (<c>{{PR_URL}}</c>).</param>
/// <param name="RepositoryKey">Stable repository key (<c>{{REPO_KEY}}</c>).</param>
/// <param name="RepositoryName">Repository display name (<c>{{REPO_NAME}}</c>).</param>
/// <param name="SourceBranch">Source branch (<c>{{SOURCE_BRANCH}}</c>).</param>
/// <param name="TargetBranch">Target branch (<c>{{TARGET_BRANCH}}</c>).</param>
/// <param name="SourceCommit">Source commit id (<c>{{SOURCE_COMMIT}}</c>).</param>
/// <param name="MergeBase">Merge base of source and target (<c>{{MERGE_BASE}}</c>).</param>
/// <param name="ChangedFiles">One <c>&lt;status&gt;\t&lt;path&gt;</c> line per changed file (<c>{{CHANGED_FILES}}</c>).</param>
/// <param name="WorktreePath">Full path of the review's worktree (<c>{{WORKTREE_PATH}}</c>).</param>
public sealed record PromptContext(
    int PullRequestId,
    string Title,
    string Description,
    string Author,
    string Url,
    string RepositoryKey,
    string RepositoryName,
    string SourceBranch,
    string TargetBranch,
    string SourceCommit,
    string MergeBase,
    string ChangedFiles,
    string WorktreePath);
