using CodebaseReviewTool.Core.Git;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>
/// Azure DevOps REST calls, for any server from TFS 2017 (REST 3.0) up to Azure DevOps Services (7.1).
/// Detects the server's API version once, identifies the authenticated user and reads pull requests. Every call is
/// read-only.
/// </summary>
public interface IDevOpsClient
{
    /// <summary>
    /// Returns the REST <c>api-version</c> used for versioned calls: <c>DevOps:ApiVersion</c> when configured, otherwise
    /// the newest version the server accepts, probed once and cached for the process lifetime. Failures (including
    /// authentication failures, which stop the probe) come back as a failed result and are not cached.
    /// </summary>
    Task<DevOpsApiVersionResult> GetApiVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user the credentials authenticate as. Never throws for HTTP or network failures; those come back
    /// as a failed <see cref="DevOpsUserResult"/>.
    /// </summary>
    Task<DevOpsUserResult> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every active pull request in <paramref name="repository"/>, reading page after page until the server
    /// returns a short page. Read-only. Never throws for HTTP, network or authentication failures; those come back as
    /// a failed result naming the HTTP status and request path.
    /// </summary>
    Task<DevOpsListResult<DevOpsPullRequest>> ListActivePullRequestsAsync(ResolvedRepository repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one pull request of <paramref name="repository"/> by id, whatever its status. Read-only. Never throws for
    /// HTTP, network or authentication failures; those come back as a failed result.
    /// </summary>
    Task<DevOpsResult<DevOpsPullRequest>> GetPullRequestAsync(ResolvedRepository repository, int id, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IDevOpsClient.GetCurrentUserAsync"/>.</summary>
/// <param name="Succeeded">True when DevOps accepted the credentials and named a user.</param>
/// <param name="DisplayName">The authenticated user's display name (or id) on success.</param>
/// <param name="Error">What went wrong on failure, including the HTTP status code when there was one.</param>
public sealed record DevOpsUserResult(bool Succeeded, string? DisplayName, string? Error)
{
    public static DevOpsUserResult Success(string displayName) => new(true, displayName, null);

    public static DevOpsUserResult Failure(string error) => new(false, null, error);
}

/// <summary>Outcome of <see cref="IDevOpsClient.GetApiVersionAsync"/>.</summary>
/// <param name="Succeeded">True when a version is known.</param>
/// <param name="Version">The api-version value, e.g. <c>3.0</c>.</param>
/// <param name="IsDetected">True when probed from the server; false when taken from <c>DevOps:ApiVersion</c>.</param>
/// <param name="Error">What went wrong on failure.</param>
public sealed record DevOpsApiVersionResult(bool Succeeded, string? Version, bool IsDetected, string? Error)
{
    public static DevOpsApiVersionResult Configured(string version) => new(true, version, false, null);

    public static DevOpsApiVersionResult Detected(string version) => new(true, version, true, null);

    public static DevOpsApiVersionResult Failure(string error) => new(false, null, false, error);
}
