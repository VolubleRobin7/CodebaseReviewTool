using System.Text;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.DevOps;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Git;

/// <summary>
/// Turns each configured <c>Repositories[n]</c> entry into a <see cref="ResolvedRepository"/> by reading the clone's
/// remote and parsing its URL for the organisation, project and repository names.
/// </summary>
public sealed class RepositoryResolver(GitRunner gitRunner, IOptions<RepositoriesOptions> repositories, IOptions<DevOpsOptions> devOps)
{
    private readonly GitRunner _gitRunner = gitRunner;
    private readonly RepositoriesOptions _repositories = repositories.Value;
    private readonly DevOpsOptions _devOps = devOps.Value;

    /// <summary>Resolves every repository, or throws <see cref="StartupValidationException"/> listing every failure.</summary>
    public async Task<IReadOnlyList<ResolvedRepository>> ResolveAllAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var resolved = new List<ResolvedRepository>();

        var configuredOrganization = _devOps.OrganizationName;
        if (configuredOrganization is null)
        {
            failures.Add($"DevOps:OrganizationUrl: cannot read an organisation from '{_devOps.OrganizationUrl}'.");
        }

        for (var i = 0; i < _repositories.Items.Count; i++)
        {
            var result = await ResolveAsync(i, _repositories.Items[i], configuredOrganization, cancellationToken);
            if (result.Repository is not null)
            {
                resolved.Add(result.Repository);
            }
            else
            {
                failures.Add(result.Failure!);
            }
        }

        foreach (var duplicate in resolved.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            failures.Add(
                $"Repositories: the display names {string.Join(", ", duplicate.Select(r => $"'{r.DisplayName}'"))} " +
                $"all produce the key '{duplicate.Key}'; keys must be unique.");
        }

        if (failures.Count > 0)
        {
            throw new StartupValidationException(failures);
        }

        return resolved;
    }

    private async Task<(ResolvedRepository? Repository, string? Failure)> ResolveAsync(
        int index,
        RepositoryOptions options,
        string? configuredOrganization,
        CancellationToken cancellationToken)
    {
        var prefix = $"Repositories:{index} ('{options.DisplayName}')";

        var key = Slug(options.DisplayName);
        if (key.Length == 0)
        {
            return (null, $"{prefix}: DisplayName must contain at least one letter or digit.");
        }

        var remotes = await _gitRunner.RunAsync(options.LocalPath, ["remote"], cancellationToken: cancellationToken);
        if (!remotes.Succeeded)
        {
            return (null, $"{prefix}: '{options.LocalPath}' is not a git repository ({FirstLine(remotes.StandardError)}).");
        }

        var remoteNames = remotes.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string remoteName;
        if (!string.IsNullOrWhiteSpace(options.Remote))
        {
            if (!remoteNames.Contains(options.Remote, StringComparer.Ordinal))
            {
                return (null, $"{prefix}: Remote '{options.Remote}' does not exist in '{options.LocalPath}' (remotes: {Describe(remoteNames)}).");
            }

            remoteName = options.Remote;
        }
        else if (remoteNames.Contains("origin", StringComparer.Ordinal))
        {
            remoteName = "origin";
        }
        else if (remoteNames.Length == 1)
        {
            remoteName = remoteNames[0];
        }
        else
        {
            return (null, $"{prefix}: cannot choose a remote in '{options.LocalPath}' (remotes: {Describe(remoteNames)}); set Repositories:{index}:Remote.");
        }

        var urlResult = await _gitRunner.RunAsync(options.LocalPath, ["remote", "get-url", remoteName], cancellationToken: cancellationToken);
        if (!urlResult.Succeeded)
        {
            return (null, $"{prefix}: cannot read the URL of remote '{remoteName}' ({FirstLine(urlResult.StandardError)}).");
        }

        // Redacted so a token embedded in an https remote never reaches the UI or a startup message.
        var remoteUrl = AzureDevOpsUrl.RedactCredentials(urlResult.StandardOutput.Trim());
        var hasOverrides = !string.IsNullOrWhiteSpace(options.Project) && !string.IsNullOrWhiteSpace(options.Repository);

        string organization;
        string project;
        string repository;
        if (AzureDevOpsUrl.TryParseRemoteUrl(remoteUrl, _devOps.OrganizationUrl, out var parts))
        {
            organization = parts.Organization;
            project = string.IsNullOrWhiteSpace(options.Project) ? parts.Project : options.Project;
            repository = string.IsNullOrWhiteSpace(options.Repository) ? parts.Repository : options.Repository;
        }
        else if (hasOverrides && configuredOrganization is not null)
        {
            organization = configuredOrganization;
            project = options.Project!;
            repository = options.Repository!;
        }
        else
        {
            return (null,
                $"{prefix}: the URL of remote '{remoteName}' ('{remoteUrl}') is not a recognised Azure DevOps URL; " +
                $"set Repositories:{index}:Project and Repositories:{index}:Repository.");
        }

        if (configuredOrganization is not null && !organization.Equals(configuredOrganization, StringComparison.OrdinalIgnoreCase))
        {
            return (null,
                $"{prefix}: remote '{remoteName}' belongs to organisation '{organization}' but DevOps:OrganizationUrl names " +
                $"organisation '{configuredOrganization}'.");
        }

        return (new ResolvedRepository(options.DisplayName, key, options.LocalPath, remoteName, organization, project, repository, remoteUrl), null);
    }

    /// <summary>Lowercase ASCII letters and digits; every run of anything else becomes one hyphen; trimmed of hyphens.</summary>
    public static string Slug(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(displayName.Length);
        var pendingHyphen = false;
        foreach (var c in displayName.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingHyphen = false;
                builder.Append(c);
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }

    private static string Describe(string[] remoteNames) => remoteNames.Length == 0 ? "none" : string.Join(", ", remoteNames);

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? "no error output" : line;
    }
}
