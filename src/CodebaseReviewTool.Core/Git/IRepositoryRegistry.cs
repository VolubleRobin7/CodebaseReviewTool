namespace CodebaseReviewTool.Core.Git;

/// <summary>The repositories resolved at startup. Populated before the web host starts listening.</summary>
public interface IRepositoryRegistry
{
    IReadOnlyList<ResolvedRepository> Repositories { get; }

    /// <summary>Finds a repository by its <see cref="ResolvedRepository.Key"/> (case-insensitive).</summary>
    ResolvedRepository? Find(string key);
}

public sealed class RepositoryRegistry : IRepositoryRegistry
{
    private volatile IReadOnlyList<ResolvedRepository> _repositories = [];

    public IReadOnlyList<ResolvedRepository> Repositories => _repositories;

    public ResolvedRepository? Find(string key) =>
        _repositories.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    internal void Set(IReadOnlyList<ResolvedRepository> repositories) => _repositories = repositories;
}
