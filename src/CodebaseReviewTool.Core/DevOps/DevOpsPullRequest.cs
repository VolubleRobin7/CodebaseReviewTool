using System.Text.Json.Serialization;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>
/// A pull request as the Azure DevOps REST API returns it (<c>GitPullRequest</c>), reduced to the fields the tool
/// uses. Deserialised from the list and get endpoints; <see cref="WebUrl"/> is filled in by the client because the
/// API's own <c>url</c> is the REST resource, not the page a person opens.
/// </summary>
public sealed class DevOpsPullRequest
{
    private const string BranchPrefix = "refs/heads/";

    [JsonPropertyName("pullRequestId")]
    public int PullRequestId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Null when the pull request has no description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdBy")]
    public DevOpsIdentityRef? CreatedBy { get; set; }

    /// <summary>Full ref name, e.g. <c>refs/heads/feature/x</c>.</summary>
    [JsonPropertyName("sourceRefName")]
    public string SourceRefName { get; set; } = string.Empty;

    /// <summary>Full ref name, e.g. <c>refs/heads/main</c>.</summary>
    [JsonPropertyName("targetRefName")]
    public string TargetRefName { get; set; } = string.Empty;

    /// <summary>The source commit of the last merge attempt; null until DevOps has computed a merge.</summary>
    [JsonPropertyName("lastMergeSourceCommit")]
    public DevOpsCommitRef? LastMergeSourceCommit { get; set; }

    /// <summary>DevOps status text: <c>active</c>, <c>completed</c> or <c>abandoned</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>UTC.</summary>
    [JsonPropertyName("creationDate")]
    public DateTime CreationDate { get; set; }

    /// <summary>The pull request page in the DevOps web UI.</summary>
    [JsonIgnore]
    public string WebUrl { get; set; } = string.Empty;

    /// <summary><see cref="SourceRefName"/> without the <c>refs/heads/</c> prefix.</summary>
    [JsonIgnore]
    public string SourceBranch => BranchName(SourceRefName);

    /// <summary><see cref="TargetRefName"/> without the <c>refs/heads/</c> prefix.</summary>
    [JsonIgnore]
    public string TargetBranch => BranchName(TargetRefName);

    private static string BranchName(string refName) =>
        refName.StartsWith(BranchPrefix, StringComparison.Ordinal) ? refName[BranchPrefix.Length..] : refName;
}

/// <summary>The part of a DevOps identity reference the tool reads.</summary>
public sealed class DevOpsIdentityRef
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

/// <summary>The part of a DevOps commit reference the tool reads.</summary>
public sealed class DevOpsCommitRef
{
    [JsonPropertyName("commitId")]
    public string? CommitId { get; set; }
}
