namespace CodebaseReviewTool.Core.Git;

/// <summary>A configured repository after its remote has been read and parsed at startup.</summary>
/// <param name="DisplayName">Name shown in the UI (from configuration).</param>
/// <param name="Key">Slug of <paramref name="DisplayName"/>; the stable id used in worktree paths and the database.</param>
/// <param name="LocalPath">Expanded path of the local clone.</param>
/// <param name="RemoteName">The git remote that was read.</param>
/// <param name="Organization">Azure DevOps organisation.</param>
/// <param name="Project">Azure DevOps project (URL-decoded).</param>
/// <param name="Repository">Azure DevOps repository name (URL-decoded).</param>
/// <param name="RemoteUrl">URL of <paramref name="RemoteName"/>.</param>
public sealed record ResolvedRepository(
    string DisplayName,
    string Key,
    string LocalPath,
    string RemoteName,
    string Organization,
    string Project,
    string Repository,
    string RemoteUrl);
