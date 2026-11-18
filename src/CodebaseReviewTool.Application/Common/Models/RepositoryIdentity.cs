namespace CodebaseReviewTool.Application.Common.Models;

/// <summary>Which repository on its host a remote or pull request URL points at.</summary>
/// <param name="Organization">The host's organisation (or collection).</param>
/// <param name="Project">The host's project.</param>
/// <param name="Repository">The repository's name.</param>
public sealed record RepositoryIdentity(string Organization, string Project, string Repository)
{
    /// <summary>True when <paramref name="repository"/> is this repository (names compared ignoring case).</summary>
    public bool Matches(ResolvedRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.Organization.Equals(Organization, StringComparison.OrdinalIgnoreCase)
            && repository.Project.Equals(Project, StringComparison.OrdinalIgnoreCase)
            && repository.Repository.Equals(Repository, StringComparison.OrdinalIgnoreCase);
    }
}
