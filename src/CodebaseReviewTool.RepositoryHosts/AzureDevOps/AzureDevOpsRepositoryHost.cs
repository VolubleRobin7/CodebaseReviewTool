using System.Diagnostics.CodeAnalysis;
using System.Net;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.RepositoryHosts.AzureDevOps;

/// <summary>
/// <see cref="IRepositoryHost"/> on Azure DevOps Services or Server: identifies repositories from their remote URLs
/// and <c>DevOps:OrganizationUrl</c>, and maps <see cref="IDevOpsClient"/>'s results to the host-neutral types.
/// </summary>
public sealed class AzureDevOpsRepositoryHost(IDevOpsClient client, IOptions<DevOpsOptions> options) : IRepositoryHost
{
    private readonly IDevOpsClient _client = client;
    private readonly DevOpsOptions _options = options.Value;

    IReadOnlyList<string> IRepositoryHost.ValidateConfiguration() =>
        _options.OrganizationName is null
            ? [$"DevOps:OrganizationUrl: cannot read an organisation from '{_options.OrganizationUrl}'."]
            : [];

    ServiceResult<RepositoryIdentity> IRepositoryHost.IdentifyRepository(CloneRemote remote)
    {
        ArgumentNullException.ThrowIfNull(remote);

        var clone = remote.Configured;
        var configuredOrganization = _options.OrganizationName;
        var hasOverrides = clone.ProjectOverride is not null && clone.RepositoryOverride is not null;

        RepositoryIdentity identity;
        if (AzureDevOpsUrl.TryParseRemoteUrl(remote.RemoteUrl, _options.OrganizationUrl, out var parts))
        {
            identity = new RepositoryIdentity(parts.Organization, clone.ProjectOverride ?? parts.Project, clone.RepositoryOverride ?? parts.Repository);
        }
        else if (hasOverrides && configuredOrganization is not null)
        {
            identity = new RepositoryIdentity(configuredOrganization, clone.ProjectOverride!, clone.RepositoryOverride!);
        }
        else
        {
            return ServiceResult<RepositoryIdentity>.Failure(
                $"{clone.Description}: the URL of remote '{remote.RemoteName}' ('{remote.RemoteUrl}') is not a recognised Azure DevOps URL; " +
                $"set {clone.ConfigurationKey}:Project and {clone.ConfigurationKey}:Repository.");
        }

        if (configuredOrganization is not null && !identity.Organization.Equals(configuredOrganization, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<RepositoryIdentity>.Failure(
                $"{clone.Description}: remote '{remote.RemoteName}' belongs to organisation '{identity.Organization}' but DevOps:OrganizationUrl names " +
                $"organisation '{configuredOrganization}'.");
        }

        return ServiceResult<RepositoryIdentity>.Success(identity);
    }

    bool IRepositoryHost.TryParsePullRequestUrl(string? url, [NotNullWhen(true)] out RepositoryIdentity? repository, out int pullRequestId)
    {
        repository = null;
        if (!AzureDevOpsUrl.TryParsePullRequestUrl(url, _options.OrganizationUrl, out var parts, out pullRequestId))
        {
            return false;
        }

        repository = new RepositoryIdentity(parts.Organization, parts.Project, parts.Repository);
        return true;
    }

    async Task<ServiceResult<IReadOnlyList<RemotePullRequest>>> IRepositoryHost.ListActivePullRequestsAsync(
        ResolvedRepository repository,
        CancellationToken cancellationToken)
    {
        var result = await _client.ListActivePullRequestsAsync(repository, cancellationToken);
        return result.Succeeded
            ? ServiceResult<IReadOnlyList<RemotePullRequest>>.Success([.. result.Items.Select(ToRemote)])
            : ServiceResult<IReadOnlyList<RemotePullRequest>>.Failure(result.Error ?? "Unknown error");
    }

    async Task<ServiceResult<RemotePullRequest>> IRepositoryHost.GetPullRequestAsync(
        ResolvedRepository repository,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        var result = await _client.GetPullRequestAsync(repository, pullRequestId, cancellationToken);
        if (result.Succeeded)
        {
            return ServiceResult<RemotePullRequest>.Success(ToRemote(result.Value!));
        }

        var error = result.Error ?? "Unknown error";
        return result.StatusCode == HttpStatusCode.NotFound
            ? ServiceResult<RemotePullRequest>.NotFound(error)
            : ServiceResult<RemotePullRequest>.Failure(error);
    }

    async Task<ServiceResult<PostedCommentThread>> IRepositoryHost.CreateCommentThreadAsync(
        ResolvedRepository repository,
        int pullRequestId,
        NewCommentThread thread,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var result = await _client.CreatePullRequestThreadAsync(
            repository, pullRequestId, new DevOpsNewThread(thread.Content, thread.FilePath, thread.Line), cancellationToken);
        return result.Succeeded
            ? ServiceResult<PostedCommentThread>.Success(new PostedCommentThread(result.Value!.Id, result.Value.IsAnchored))
            : ServiceResult<PostedCommentThread>.Failure(result.Error ?? "Unknown error");
    }

    /// <summary>A pull request as the cache stores it; DevOps calls an open pull request <c>active</c>.</summary>
    private static RemotePullRequest ToRemote(DevOpsPullRequest source) => new(
        Id: source.PullRequestId,
        Title: source.Title,
        Description: source.Description,
        AuthorName: source.CreatedBy?.DisplayName ?? string.Empty,
        SourceBranch: source.SourceBranch,
        TargetBranch: source.TargetBranch,
        SourceCommit: source.LastMergeSourceCommit?.CommitId,
        Status: source.Status,
        IsActive: string.Equals(source.Status, "active", StringComparison.OrdinalIgnoreCase),
        IsDraft: source.IsDraft,
        MergeStatus: source.MergeStatus,
        CreatedAt: source.CreationDate,
        WebUrl: source.WebUrl);
}
