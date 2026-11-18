using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodebaseReviewTool.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.RepositoryHosts.AzureDevOps;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Azure DevOps REST API, from TFS 2017 (REST 3.0) to Azure DevOps Services
/// (7.1). Authenticates with PAT basic auth, or with the Windows account when <c>DevOps:UseDefaultCredentials</c> is
/// set (the primary handler is configured for that in <c>AddRepositoryHosts</c>). Every versioned call must go through
/// <see cref="GetVersionedAsync"/> so it carries the configured or detected api-version.
/// </summary>
internal sealed partial class DevOpsClient : IDevOpsClient
{
    /// <summary>
    /// Newest first; the probe keeps the first one the server accepts. 3.0 (TFS 2017) is the floor because it is
    /// the first server that issues personal access tokens.
    /// </summary>
    internal static readonly string[] ProbeVersions = ["7.1", "6.0", "5.0", "4.1", "3.0"];

    // connectionData is unversioned on purpose: without api-version it answers on every server version.
    private const string ConnectionDataPath = "_apis/connectionData";

    // Project-less repository list: needs only Code (Read) and exists on every version.
    private const string ProbePath = "_apis/git/repositories";

    // The PAT scopes named when authentication fails: reading needs the first, posting comment threads the second.
    private const string ReadScope = "Code (Read)";
    private const string WriteScope = "Pull Request Threads (Read & write)";

    // Page size for list calls; a page shorter than this is the last one.
    private const int PageSize = 100;

    // Guards against a server that never returns a short page.
    private const int MaxPages = 100;

    private readonly HttpClient _httpClient;
    private readonly DevOpsOptions _options;
    private readonly DevOpsApiVersionCache _versionCache;
    private readonly ILogger<DevOpsClient> _logger;

    public DevOpsClient(HttpClient httpClient, IOptions<DevOpsOptions> options, DevOpsApiVersionCache versionCache, ILogger<DevOpsClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _versionCache = versionCache;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.OrganizationUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // With Windows auth the handler negotiates; a Basic header would pre-empt that, so the PAT is not sent.
        if (!_options.UseDefaultCredentials && !string.IsNullOrEmpty(_options.Pat))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + _options.Pat));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    public Task<DevOpsApiVersionResult> GetApiVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiVersion))
        {
            return Task.FromResult(DevOpsApiVersionResult.Configured(_options.ApiVersion.Trim()));
        }

        if (MissingCredentials() is { } missing)
        {
            return Task.FromResult(DevOpsApiVersionResult.Failure(missing));
        }

        return _versionCache.GetOrDetectAsync(DetectApiVersionAsync, cancellationToken);
    }

    public async Task<DevOpsUserResult> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (MissingCredentials() is { } missing)
        {
            return DevOpsUserResult.Failure(missing);
        }

        try
        {
            using var response = await _httpClient.GetAsync(ConnectionDataPath, cancellationToken);
            if (AuthOrFormatFailure(response, ConnectionDataPath) is { } failure)
            {
                return DevOpsUserResult.Failure(failure);
            }

            if (!response.IsSuccessStatusCode)
            {
                return DevOpsUserResult.Failure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {ConnectionDataPath}.");
            }

            var data = await response.Content.ReadFromJsonAsync<ConnectionData>(cancellationToken);
            var user = data?.AuthenticatedUser;
            var name = FirstNonEmpty(user?.ProviderDisplayName, user?.CustomDisplayName, user?.Id);

            return name is null
                ? DevOpsUserResult.Failure("connectionData returned no authenticated user.")
                : DevOpsUserResult.Success(name);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "DevOps connectionData call failed");
            return DevOpsUserResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DevOps connectionData call timed out");
            return DevOpsUserResult.Failure($"Timed out after {_httpClient.Timeout.TotalSeconds:0} s calling {ConnectionDataPath}.");
        }
    }

    public async Task<DevOpsListResult<DevOpsPullRequest>> ListActivePullRequestsAsync(
        ResolvedRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (MissingCredentials() is { } missing)
        {
            return DevOpsListResult<DevOpsPullRequest>.Failure(missing);
        }

        var basePath = PullRequestsPath(repository);
        var items = new List<DevOpsPullRequest>();
        for (var page = 0; page < MaxPages; page++)
        {
            var path = $"{basePath}?searchCriteria.status=active&$top={PageSize}&$skip={page * PageSize}";
            var result = await GetJsonAsync<ListResponse<DevOpsPullRequest>>(path, cancellationToken);
            if (!result.Succeeded)
            {
                return DevOpsListResult<DevOpsPullRequest>.Failure(result.Error!);
            }

            var pageItems = result.Value!.Value ?? [];
            items.AddRange(pageItems);
            if (pageItems.Count < PageSize)
            {
                // A pull request created while paging can shift into the next page and appear twice.
                var distinct = items.DistinctBy(p => p.PullRequestId).ToList();
                distinct.ForEach(p => p.WebUrl = PullRequestWebUrl(repository, p.PullRequestId));
                return DevOpsListResult<DevOpsPullRequest>.Success(distinct);
            }
        }

        return DevOpsListResult<DevOpsPullRequest>.Failure(
            $"More than {MaxPages * PageSize} active pull requests from {basePath}; stopped paging.");
    }

    public async Task<DevOpsResult<DevOpsPullRequest>> GetPullRequestAsync(
        ResolvedRepository repository,
        int id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (MissingCredentials() is { } missing)
        {
            return DevOpsResult<DevOpsPullRequest>.Failure(missing);
        }

        var result = await GetJsonAsync<DevOpsPullRequest>($"{PullRequestsPath(repository)}/{id}", cancellationToken);
        if (result.Succeeded)
        {
            result.Value!.WebUrl = PullRequestWebUrl(repository, result.Value.PullRequestId);
        }

        return result;
    }

    public async Task<DevOpsResult<DevOpsCommentThread>> CreatePullRequestThreadAsync(
        ResolvedRepository repository,
        int pullRequestId,
        DevOpsNewThread thread,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentException.ThrowIfNullOrWhiteSpace(thread.Content);
        if (MissingCredentials() is { } missing)
        {
            return DevOpsResult<DevOpsCommentThread>.Failure(missing);
        }

        var path = $"{PullRequestsPath(repository)}/{pullRequestId}/threads";
        var anchor = ThreadContext.For(thread);

        var (result, status) = await PostJsonAsync<NewThreadRequest, DevOpsCommentThread>(
            path,
            NewThreadRequest.Create(thread.Content, anchor),
            cancellationToken);

        // DevOps refuses an anchor it cannot place, such as a file the pull request does not touch. The comment still
        // carries the location in its text, so it is posted on the pull request as a whole rather than lost.
        if (!result.Succeeded && anchor is not null && status == HttpStatusCode.BadRequest)
        {
            _logger.LogInformation("DevOps refused the anchor {FilePath} on pull request {PullRequestId}; posting unanchored. {Error}",
                anchor.FilePath, pullRequestId, result.Error);

            (result, _) = await PostJsonAsync<NewThreadRequest, DevOpsCommentThread>(
                path,
                NewThreadRequest.Create(thread.Content, null),
                cancellationToken);
            anchor = null;
        }

        if (result.Succeeded)
        {
            result.Value!.IsAnchored = anchor is not null;
        }

        return result;
    }

    /// <summary>Appends <c>api-version=&lt;version&gt;</c> to a relative path that may already have a query string.</summary>
    internal static string WithApiVersion(string pathAndQuery, string apiVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathAndQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);

        var separator = pathAndQuery.Contains('?') ? '&' : '?';
        return $"{pathAndQuery}{separator}api-version={Uri.EscapeDataString(apiVersion)}";
    }

    /// <summary>
    /// GETs a versioned endpoint with the configured or detected api-version. Every versioned call (pull request
    /// list, pull request details) goes through here so they all use the same version. Throws
    /// <see cref="HttpRequestException"/> when no version can be determined.
    /// </summary>
    internal async Task<HttpResponseMessage> GetVersionedAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        var version = await GetApiVersionAsync(cancellationToken);
        if (!version.Succeeded)
        {
            throw new HttpRequestException($"Cannot call {pathAndQuery}: {version.Error}");
        }

        return await _httpClient.GetAsync(WithApiVersion(pathAndQuery, version.Version!), cancellationToken);
    }

    /// <summary>
    /// POSTs <paramref name="body"/> as JSON to a versioned endpoint with the configured or detected api-version. Some
    /// endpoints were still in preview on older servers (pull request threads on TFS 2017 among them) and reject the
    /// plain version there, so a 400 that names the api-version is retried once with <c>-preview</c> appended. Throws
    /// <see cref="HttpRequestException"/> when no version can be determined.
    /// </summary>
    internal async Task<HttpResponseMessage> PostVersionedAsync<TBody>(string pathAndQuery, TBody body, CancellationToken cancellationToken)
    {
        var version = await GetApiVersionAsync(cancellationToken);
        if (!version.Succeeded)
        {
            throw new HttpRequestException($"Cannot call {pathAndQuery}: {version.Error}");
        }

        var response = await _httpClient.PostAsJsonAsync(WithApiVersion(pathAndQuery, version.Version!), body, cancellationToken);
        if (response.StatusCode != HttpStatusCode.BadRequest
            || version.Version!.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        // Reading the body buffers it, so the caller can still read it when this is not a version rejection.
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!ApiVersionRejectedRegex().IsMatch(text))
        {
            return response;
        }

        response.Dispose();
        return await _httpClient.PostAsJsonAsync(WithApiVersion(pathAndQuery, version.Version + "-preview"), body, cancellationToken);
    }

    private async Task<DevOpsApiVersionResult> DetectApiVersionAsync(CancellationToken cancellationToken)
    {
        var rejected = new List<string>();
        try
        {
            foreach (var version in ProbeVersions)
            {
                var path = WithApiVersion(ProbePath, version);
                using var response = await _httpClient.GetAsync(path, cancellationToken);

                // Authentication problems are the same at every version: stop rather than keep probing.
                if (AuthOrFormatFailure(response, ProbePath) is { } failure)
                {
                    return DevOpsApiVersionResult.Failure(failure);
                }

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Detected DevOps REST api-version {ApiVersion}", version);
                    return DevOpsApiVersionResult.Detected(version);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.BadRequest && ApiVersionRejectedRegex().IsMatch(body))
                {
                    rejected.Add(version);
                    continue;
                }

                return DevOpsApiVersionResult.Failure(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {path} while detecting the api-version.");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "DevOps api-version probe failed");
            return DevOpsApiVersionResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DevOps api-version probe timed out");
            return DevOpsApiVersionResult.Failure($"Timed out after {_httpClient.Timeout.TotalSeconds:0} s calling {ProbePath}.");
        }

        return DevOpsApiVersionResult.Failure(
            $"The server rejected every api-version ({string.Join(", ", rejected)}); set DevOps:ApiVersion explicitly.");
    }

    /// <summary>
    /// GETs a versioned endpoint and deserialises its JSON body. Authentication, HTTP, network, timeout and JSON
    /// failures come back as a failed result naming the HTTP status (when there was one) and the request path.
    /// </summary>
    private async Task<DevOpsResult<T>> GetJsonAsync<T>(string pathAndQuery, CancellationToken cancellationToken)
        where T : class
    {
        var (result, status) = await SendJsonAsync<T>(pathAndQuery, ReadScope, () => GetVersionedAsync(pathAndQuery, cancellationToken), cancellationToken);
        return result with { StatusCode = status };
    }

    /// <summary>
    /// POSTs <paramref name="body"/> as JSON to a versioned endpoint and deserialises the JSON reply, failing as
    /// <see cref="GetJsonAsync{T}"/> does. Also returns the HTTP status, when there was a response, so the caller can
    /// react to a particular refusal.
    /// </summary>
    private Task<(DevOpsResult<TResponse> Result, HttpStatusCode? Status)> PostJsonAsync<TBody, TResponse>(
        string pathAndQuery,
        TBody body,
        CancellationToken cancellationToken)
        where TResponse : class =>
        SendJsonAsync<TResponse>(pathAndQuery, WriteScope, () => PostVersionedAsync(pathAndQuery, body, cancellationToken), cancellationToken);

    /// <summary>
    /// Sends one versioned request and deserialises its JSON reply. <paramref name="scope"/> is the PAT scope the call
    /// needs, named in the message when authentication fails.
    /// </summary>
    private async Task<(DevOpsResult<T> Result, HttpStatusCode? Status)> SendJsonAsync<T>(
        string pathAndQuery,
        string scope,
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
        where T : class
    {
        var path = SanitisedPath(pathAndQuery);
        try
        {
            using var response = await send();
            if (AuthOrFormatFailure(response, path, scope) is { } failure)
            {
                return (DevOpsResult<T>.Failure(failure), response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ServerMessageAsync(response, cancellationToken);
                return (DevOpsResult<T>.Failure($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {path}.{detail}"), response.StatusCode);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? (DevOpsResult<T>.Failure($"HTTP {(int)response.StatusCode} with an empty body from {path}."), response.StatusCode)
                : (DevOpsResult<T>.Success(value), response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Callers decide how loudly to report the failure; the poller logs it at Warning.
            _logger.LogDebug(ex, "DevOps call to {Path} failed", path);
            return (DevOpsResult<T>.Failure($"{ex.GetType().Name} from {path}: {ex.Message}"), null);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "DevOps call to {Path} timed out", path);
            return (DevOpsResult<T>.Failure($"Timed out after {_httpClient.Timeout.TotalSeconds:0} s calling {path}."), null);
        }
    }

    /// <summary>
    /// The <c>message</c> DevOps puts in an error body (such as a TF error code and its explanation), with a leading
    /// space; empty when the body has none.
    /// </summary>
    private static async Task<string> ServerMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString())
                ? " " + message.GetString()!.Trim()
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>The repository's pull request collection, relative to the organisation URL, with names escaped.</summary>
    private static string PullRequestsPath(ResolvedRepository repository) =>
        $"{Uri.EscapeDataString(repository.Project)}/_apis/git/repositories/{Uri.EscapeDataString(repository.Repository)}/pullrequests";

    /// <summary>The pull request's page in the DevOps web UI.</summary>
    private string PullRequestWebUrl(ResolvedRepository repository, int id) =>
        $"{_options.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(repository.Project)}/_git/{Uri.EscapeDataString(repository.Repository)}/pullrequest/{id}";

    /// <summary>The request path without its query string, for error messages and logs.</summary>
    private static string SanitisedPath(string pathAndQuery)
    {
        var query = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? pathAndQuery : pathAndQuery[..query];
    }

    private string? MissingCredentials() => _options.HasCredentials
        ? null
        : "DevOps:Pat is not set (dotnet user-secrets set \"DevOps:Pat\" \"<token>\"), and DevOps:UseDefaultCredentials is false.";

    /// <summary>401/403, or a 2xx that is not JSON (an invalid PAT can yield 203 with the HTML sign-in page).</summary>
    private string? AuthOrFormatFailure(HttpResponseMessage response, string path, string scope = ReadScope)
    {
        var credential = _options.UseDefaultCredentials ? "the Windows account" : "the PAT";

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var needs = _options.UseDefaultCredentials ? "permission to " + (scope == WriteScope ? "contribute to pull requests" : "read code") : scope;
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {path}: authentication failed; check {credential} (needs {needs}).";
        }

        if (response.IsSuccessStatusCode
            && response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return $"HTTP {(int)response.StatusCode} with a non-JSON response from {path} (usually the sign-in page): {credential} is invalid or expired.";
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    // TFS answers an unsupported version with 400 and a message such as "api-version ... is not supported" or
    // "The requested REST API version of 7.1 is out of range for this server".
    [GeneratedRegex(@"api[- ]?version|VersionOutOfRange", RegexOptions.IgnoreCase)]
    private static partial Regex ApiVersionRejectedRegex();

    /// <summary>The envelope DevOps wraps every list response in.</summary>
    private sealed class ListResponse<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    private sealed class ConnectionData
    {
        [JsonPropertyName("authenticatedUser")]
        public Identity? AuthenticatedUser { get; set; }
    }

    private sealed class Identity
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("providerDisplayName")]
        public string? ProviderDisplayName { get; set; }

        [JsonPropertyName("customDisplayName")]
        public string? CustomDisplayName { get; set; }
    }

    /// <summary>The body that creates a pull request thread with one comment.</summary>
    private sealed class NewThreadRequest
    {
        // CommentThreadStatus.Active and CommentType.Text; numbers are accepted by every server version.
        private const int ActiveStatus = 1;
        private const int TextComment = 1;

        [JsonPropertyName("comments")]
        public required List<NewComment> Comments { get; init; }

        [JsonPropertyName("status")]
        public int Status { get; init; } = ActiveStatus;

        [JsonPropertyName("threadContext")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ThreadContext? ThreadContext { get; init; }

        public static NewThreadRequest Create(string content, ThreadContext? anchor) => new()
        {
            Comments = [new NewComment { Content = content, CommentType = TextComment }],
            ThreadContext = anchor,
        };
    }

    private sealed class NewComment
    {
        [JsonPropertyName("parentCommentId")]
        public int ParentCommentId { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }

        [JsonPropertyName("commentType")]
        public int CommentType { get; init; }
    }

    /// <summary>
    /// Where a thread sits: a file, and optionally a line on the right-hand (changed) side of its diff. Without an
    /// iteration context DevOps places it against the pull request's latest iteration.
    /// </summary>
    private sealed class ThreadContext
    {
        [JsonPropertyName("filePath")]
        public required string FilePath { get; init; }

        [JsonPropertyName("rightFileStart")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FilePosition? RightFileStart { get; init; }

        [JsonPropertyName("rightFileEnd")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FilePosition? RightFileEnd { get; init; }

        /// <summary>The anchor for <paramref name="thread"/>, or null when it names no file.</summary>
        public static ThreadContext? For(DevOpsNewThread thread)
        {
            if (thread.ServerFilePath is not { } filePath)
            {
                return null;
            }

            var position = thread.Line is { } line && line > 0 ? new FilePosition { Line = line, Offset = 1 } : null;
            return new ThreadContext { FilePath = filePath, RightFileStart = position, RightFileEnd = position };
        }
    }

    private sealed class FilePosition
    {
        [JsonPropertyName("line")]
        public int Line { get; init; }

        [JsonPropertyName("offset")]
        public int Offset { get; init; }
    }
}
