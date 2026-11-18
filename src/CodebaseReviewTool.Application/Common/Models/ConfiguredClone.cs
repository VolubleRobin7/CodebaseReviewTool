namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>One configured local clone, as configuration describes it, before anything has been read from it.</summary>
/// <param name="ConfigurationKey">Where it is configured, such as <c>Repositories:0</c>; error messages name it.</param>
/// <param name="DisplayName">Name shown in the UI.</param>
/// <param name="LocalPath">Expanded path of the local clone.</param>
/// <param name="RemoteName">The git remote to review through; null to use <c>origin</c>, else the only remote.</param>
/// <param name="ProjectOverride">The host project to use when the remote URL cannot be read; null to read it from the URL.</param>
/// <param name="RepositoryOverride">The host repository to use when the remote URL cannot be read; null to read it from the URL.</param>
public sealed record ConfiguredClone(
    string ConfigurationKey,
    string DisplayName,
    string LocalPath,
    string? RemoteName,
    string? ProjectOverride,
    string? RepositoryOverride)
{
    /// <summary>How error messages refer to the clone, such as <c>Repositories:0 ('My Repo')</c>.</summary>
    public string Description => $"{ConfigurationKey} ('{DisplayName}')";
}

/// <summary>A configured clone with the remote it is reviewed through.</summary>
/// <param name="Configured">The configured clone.</param>
/// <param name="RemoteName">The git remote chosen: the configured one, else <c>origin</c>, else the only remote.</param>
/// <param name="RemoteUrl">The remote's URL, with any embedded password removed.</param>
public sealed record CloneRemote(ConfiguredClone Configured, string RemoteName, string RemoteUrl);
