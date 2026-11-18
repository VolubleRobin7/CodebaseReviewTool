using CodebaseReviewTool.Infrastructure.Configuration;
using CodebaseReviewTool.Infrastructure.Processes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Git;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the git and repository options, <see cref="GitRunner"/> and <see cref="WorktreeService"/>.</summary>
    public static IServiceCollection AddGit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GitOptions>()
            .Bind(configuration.GetSection(GitOptions.SectionName))
            .PostConfigure(o => o.WorktreeRoot = PathExpansion.Expand(o.WorktreeRoot))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The section is a root-level array, so bind it into the wrapper's list.
        services.AddOptions<RepositoriesOptions>()
            .Configure(o => configuration.GetSection(RepositoriesOptions.SectionName).Bind(o.Items))
            .PostConfigure(o =>
            {
                foreach (var repository in o.Items)
                {
                    repository.LocalPath = PathExpansion.Expand(repository.LocalPath);
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Per-item repository validation and that each LocalPath exists, beyond the data annotations.
        services.AddSingleton<IValidateOptions<RepositoriesOptions>, RepositoriesOptionsValidator>();

        // Agents registers the same runner; whichever is first wins.
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<GitRunner>();
        services.AddSingleton<WorktreeService>();

        return services;
    }
}
