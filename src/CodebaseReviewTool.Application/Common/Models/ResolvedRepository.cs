namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>A configured repository after its remote has been read and identified at startup.</summary>
/// <param name="DisplayName">Name shown in the UI (from configuration).</param>
/// <param name="Key">Slug of <paramref name="DisplayName"/>; the stable id used in worktree paths and the database.</param>
/// <param name="LocalPath">Expanded path of the local clone.</param>
/// <param name="RemoteName">The git remote that was read.</param>
/// <param name="Organization">The host's organisation (or collection) the repository belongs to.</param>
/// <param name="Project">The host's project (URL-decoded).</param>
/// <param name="Repository">The host's repository name (URL-decoded).</param>
/// <param name="RemoteUrl">URL of <paramref name="RemoteName"/>, with any embedded password removed.</param>
public sealed record ResolvedRepository(
    string DisplayName,
    string Key,
    string LocalPath,
    string RemoteName,
    string Organization,
    string Project,
    string Repository,
    string RemoteUrl);
