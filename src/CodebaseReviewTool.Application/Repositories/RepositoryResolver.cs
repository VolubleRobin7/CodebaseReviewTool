using System.Text;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Common.Models;
using CodebaseReviewTool.Application.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseReviewTool.Application.Repositories;

/// <summary>
/// Turns each configured clone into a <see cref="ResolvedRepository"/>: source control reads the clone's remote, and
/// the repository host works out which of its repositories that remote is.
/// </summary>
public sealed class RepositoryResolver(ISourceControl sourceControl, IServiceScopeFactory scopeFactory)
{
    private readonly ISourceControl _sourceControl = sourceControl;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    /// <summary>Resolves every repository, or throws <see cref="StartupValidationException"/> listing every failure.</summary>
    public async Task<IReadOnlyList<ResolvedRepository>> ResolveAllAsync(CancellationToken cancellationToken)
    {
        // The host is transient (it wraps a typed HttpClient), so it is resolved for this one run.
        using var scope = _scopeFactory.CreateScope();
        var host = scope.ServiceProvider.GetRequiredService<IRepositoryHost>();

        var failures = new List<string>(host.ValidateConfiguration());
        var resolved = new List<ResolvedRepository>();

        foreach (var clone in _sourceControl.ConfiguredClones)
        {
            var result = await ResolveAsync(clone, host, cancellationToken);
            if (result.Succeeded)
            {
                resolved.Add(result.Value!);
            }
            else
            {
                failures.Add(result.Error!);
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

    private async Task<ServiceResult<ResolvedRepository>> ResolveAsync(ConfiguredClone clone, IRepositoryHost host, CancellationToken cancellationToken)
    {
        var key = Slug(clone.DisplayName);
        if (key.Length == 0)
        {
            return ServiceResult<ResolvedRepository>.Failure($"{clone.Description}: DisplayName must contain at least one letter or digit.");
        }

        var remote = await _sourceControl.ReadRemoteAsync(clone, cancellationToken);
        if (!remote.Succeeded)
        {
            return ServiceResult<ResolvedRepository>.Failure(remote.Error!);
        }

        var identity = host.IdentifyRepository(remote.Value!);
        if (!identity.Succeeded)
        {
            return ServiceResult<ResolvedRepository>.Failure(identity.Error!);
        }

        var (organization, project, repository) = identity.Value!;
        return ServiceResult<ResolvedRepository>.Success(new ResolvedRepository(
            clone.DisplayName, key, clone.LocalPath, remote.Value!.RemoteName, organization, project, repository, remote.Value.RemoteUrl));
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
}
