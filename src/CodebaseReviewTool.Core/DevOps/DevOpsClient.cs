using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodebaseReviewTool.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Azure DevOps REST API, from TFS 2017 (REST 3.0) to Azure DevOps Services
/// (7.1). Authenticates with PAT basic auth, or with the Windows account when <c>DevOps:UseDefaultCredentials</c> is
/// set (the primary handler is configured for that in <c>AddReviewCore</c>). Every versioned call must go through
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

    private string? MissingCredentials() => _options.HasCredentials
        ? null
        : "DevOps:Pat is not set (dotnet user-secrets set \"DevOps:Pat\" \"<token>\"), and DevOps:UseDefaultCredentials is false.";

    /// <summary>401/403, or a 2xx that is not JSON (an invalid PAT can yield 203 with the HTML sign-in page).</summary>
    private string? AuthOrFormatFailure(HttpResponseMessage response, string path)
    {
        var credential = _options.UseDefaultCredentials ? "the Windows account" : "the PAT";

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {path}: authentication failed; check {credential} (needs Code (Read)).";
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
}
