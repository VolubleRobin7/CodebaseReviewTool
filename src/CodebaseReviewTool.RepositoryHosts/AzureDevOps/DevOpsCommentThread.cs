using System.Text.Json.Serialization;

namespace CodebaseReviewTool.RepositoryHosts.AzureDevOps;

/// <summary>A comment thread to create on a pull request.</summary>
/// <param name="Content">The comment's text, in markdown.</param>
/// <param name="FilePath">
/// The file to anchor the thread to, relative to the repository root (either slash); null or empty for a thread on the
/// pull request as a whole.
/// </param>
/// <param name="Line">The 1-based line in the file's latest version to anchor to; null for the whole file.</param>
public sealed record DevOpsNewThread(string Content, string? FilePath, int? Line)
{
    /// <summary>
    /// <see cref="FilePath"/> as DevOps expects it, with forward slashes and one leading slash (<c>/src/Foo.cs</c>);
    /// null when there is no file.
    /// </summary>
    public string? ServerFilePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return null;
            }

            var path = FilePath.Trim().Replace('\\', '/');
            while (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            return "/" + path.TrimStart('/');
        }
    }
}

/// <summary>
/// A comment thread as the Azure DevOps REST API returns it (<c>GitPullRequestCommentThread</c>), reduced to the fields
/// the tool uses.
/// </summary>
public sealed class DevOpsCommentThread
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// True when the thread is anchored to its file; false when it had no file or the server refused the anchor and
    /// it was posted on the pull request as a whole. Set by the client, not read from the response.
    /// </summary>
    [JsonIgnore]
    public bool IsAnchored { get; set; }
}
