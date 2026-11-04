using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CodebaseReviewTool.Core.DevOps;

/// <summary>
/// Parsing of Azure DevOps organisation URLs and git remote URLs. Covers Azure DevOps Services
/// (<c>dev.azure.com</c>, legacy <c>visualstudio.com</c>) and Azure DevOps Server, whose collection URL
/// (<c>http://server:8080/tfs/Collection</c>) plays the part of the organisation URL.
/// </summary>
public static partial class AzureDevOpsUrl
{
    /// <summary>The parts of an Azure DevOps git remote URL, URL-decoded.</summary>
    public sealed record RemoteParts(string Organization, string Project, string Repository);

    /// <summary>
    /// Reads the organisation from <c>https://dev.azure.com/&lt;org&gt;</c>, <c>https://&lt;org&gt;.visualstudio.com</c>
    /// or, for Azure DevOps Server, the last path segment of the collection URL
    /// (<c>http://server[:port]/[tfs/]&lt;collection&gt;</c>).
    /// </summary>
    public static bool TryParseOrganizationUrl(string? url, [NotNullWhen(true)] out string? organization)
    {
        organization = null;
        if (!TryCreateHttpUri(url, out var uri))
        {
            return false;
        }

        var segments = Segments(uri);
        if (IsDevAzureHost(uri.Host))
        {
            organization = segments.Length > 0 ? Decode(segments[0]) : null;
        }
        else if (TryVisualStudioOrganization(uri.Host, out var fromHost))
        {
            organization = fromHost;
        }
        else
        {
            // Azure DevOps Server: the collection is the last segment of the collection URL.
            organization = segments.Length > 0 ? Decode(segments[^1]) : null;
        }

        return !string.IsNullOrEmpty(organization);
    }

    /// <summary>
    /// Parses a git remote URL in any of these forms:
    /// <list type="bullet">
    /// <item><c>https://dev.azure.com/&lt;org&gt;/&lt;project&gt;/_git/&lt;repo&gt;</c> (optionally with <c>&lt;user&gt;@</c>)</item>
    /// <item><c>git@ssh.dev.azure.com:v3/&lt;org&gt;/&lt;project&gt;/&lt;repo&gt;</c></item>
    /// <item><c>https://&lt;org&gt;.visualstudio.com/[DefaultCollection/]&lt;project&gt;/_git/&lt;repo&gt;</c></item>
    /// <item>Azure DevOps Server, when <paramref name="organizationUrl"/> is a collection URL:
    /// <c>&lt;collection URL&gt;/&lt;project&gt;/_git/&lt;repo&gt;</c> over http(s) or <c>ssh://</c> on the same host.</item>
    /// </list>
    /// A URL without a project segment (<c>.../_git/&lt;repo&gt;</c>) means the project has the repository's name.
    /// </summary>
    public static bool TryParseRemoteUrl(string? remoteUrl, string? organizationUrl, [NotNullWhen(true)] out RemoteParts? parts)
    {
        parts = null;
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return false;
        }

        var text = remoteUrl.Trim();

        var ssh = SshRemoteRegex().Match(text);
        if (ssh.Success)
        {
            parts = new RemoteParts(Decode(ssh.Groups["org"].Value), Decode(ssh.Groups["project"].Value), Decode(ssh.Groups["repo"].Value));
            return true;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = Segments(uri);
        var isHttp = uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;

        if (isHttp && IsDevAzureHost(uri.Host))
        {
            return segments.Length > 0 && TryParseProjectAndRepository(Decode(segments[0]), segments[1..], out parts);
        }

        if (isHttp && TryVisualStudioOrganization(uri.Host, out var fromHost))
        {
            var rest = segments.Length > 0 && segments[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase)
                ? segments[1..]
                : segments;
            return TryParseProjectAndRepository(fromHost, rest, out parts);
        }

        return TryParseServerRemote(uri, segments, organizationUrl, out parts);
    }

    /// <summary>
    /// Removes a password or token embedded in an http(s) URL (<c>https://user:secret@host/...</c> becomes
    /// <c>https://user@host/...</c>) so the URL is safe to display or log.
    /// </summary>
    public static string RedactCredentials(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.UserInfo.Contains(':'))
        {
            return url;
        }

        var builder = new UriBuilder(uri) { Password = string.Empty };
        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    /// <summary>Azure DevOps Server: the remote must sit under the collection path on the collection's host.</summary>
    private static bool TryParseServerRemote(Uri remote, string[] remoteSegments, string? organizationUrl, [NotNullWhen(true)] out RemoteParts? parts)
    {
        parts = null;
        var isSupportedScheme = remote.Scheme is "http" or "https" or "ssh";
        if (!isSupportedScheme
            || !TryCreateHttpUri(organizationUrl, out var collection)
            || IsDevAzureHost(collection.Host)
            || TryVisualStudioOrganization(collection.Host, out _)
            || !remote.Host.Equals(collection.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var collectionSegments = Segments(collection);
        if (collectionSegments.Length == 0 || remoteSegments.Length <= collectionSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < collectionSegments.Length; i++)
        {
            if (!Decode(remoteSegments[i]).Equals(Decode(collectionSegments[i]), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return TryParseProjectAndRepository(Decode(remoteSegments[collectionSegments.Length - 1]), remoteSegments[collectionSegments.Length..], out parts);
    }

    /// <summary>Parses <c>&lt;project&gt;/_git/&lt;repo&gt;</c> or <c>_git/&lt;repo&gt;</c> (project named after the repository).</summary>
    private static bool TryParseProjectAndRepository(string organization, string[] rest, [NotNullWhen(true)] out RemoteParts? parts)
    {
        parts = null;
        string project;
        string repository;

        if (rest.Length == 3 && IsGitSegment(rest[1]))
        {
            project = Decode(rest[0]);
            repository = Decode(rest[2]);
        }
        else if (rest.Length == 2 && IsGitSegment(rest[0]))
        {
            repository = Decode(rest[1]);
            project = repository;
        }
        else
        {
            return false;
        }

        if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(project) || string.IsNullOrEmpty(repository))
        {
            return false;
        }

        parts = new RemoteParts(organization, project, repository);
        return true;
    }

    private static bool IsGitSegment(string segment) => segment.Equals("_git", StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateHttpUri(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static bool IsDevAzureHost(string host) => host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase);

    private static bool TryVisualStudioOrganization(string host, out string organization)
    {
        const string suffix = ".visualstudio.com";
        if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
        {
            organization = host[..^suffix.Length];
            return !organization.Contains('.');
        }

        organization = string.Empty;
        return false;
    }

    // AbsolutePath keeps the escaping (e.g. %20), which Decode removes per segment.
    private static string[] Segments(Uri uri) => uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string Decode(string segment) => Uri.UnescapeDataString(segment);

    [GeneratedRegex(@"^(?:[^@/]+@)?(?:ssh\.dev\.azure\.com|vs-ssh\.visualstudio\.com):v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SshRemoteRegex();
}
