using System.Diagnostics.CodeAnalysis;
using CodebaseReviewTool.Application.Common.Models;

namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>
/// The service the reviewed repositories live on (Azure DevOps today): it identifies repositories from their remote
/// URLs, reports their pull requests and takes comment threads. Calls to the host never throw for HTTP, network or
/// authentication failures; those come back as a failed <see cref="ServiceResult{T}"/>.
/// </summary>
public interface IRepositoryHost
{
    /// <summary>Problems with the host's own configuration that stop every repository resolving; empty when none.</summary>
    IReadOnlyList<string> ValidateConfiguration();

    /// <summary>
    /// Works out which repository on the host a clone's remote points at, using the clone's configured project and
    /// repository when the URL cannot be read. Fails, with a message naming the clone, when it is not on this host.
    /// </summary>
    ServiceResult<RepositoryIdentity> IdentifyRepository(CloneRemote remote);

    /// <summary>Reads the repository and pull request id out of a pull request's page URL; false when it is not one.</summary>
    bool TryParsePullRequestUrl(string? url, [NotNullWhen(true)] out RepositoryIdentity? repository, out int pullRequestId);

    /// <summary>Lists every active pull request in <paramref name="repository"/>.</summary>
    Task<ServiceResult<IReadOnlyList<RemotePullRequest>>> ListActivePullRequestsAsync(ResolvedRepository repository, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one pull request by id, whatever its status. A pull request that does not exist comes back with
    /// <see cref="ServiceResult{T}.IsNotFound"/> set.
    /// </summary>
    Task<ServiceResult<RemotePullRequest>> GetPullRequestAsync(ResolvedRepository repository, int pullRequestId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an active comment thread on a pull request, anchored to the thread's file and line when it has them.
    /// When the host refuses the anchor, the thread is posted on the pull request as a whole instead.
    /// </summary>
    Task<ServiceResult<PostedCommentThread>> CreateCommentThreadAsync(
        ResolvedRepository repository,
        int pullRequestId,
        NewCommentThread thread,
        CancellationToken cancellationToken);
}
