using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.DevOps;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Hosting;
using CodebaseReviewTool.Core.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything Core provides: options, the DbContext factory, the DevOps HttpClient,
    /// the review services and the hosted services.
    /// </summary>
    public static IServiceCollection AddReviewCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<GitRunner>();
        services.AddSingleton<RepositoryResolver>();
        services.AddSingleton<RepositoryRegistry>();
        services.AddSingleton<IRepositoryRegistry>(sp => sp.GetRequiredService<RepositoryRegistry>());

        services.AddDbContextFactory<ReviewDbContext>((sp, options) =>
        {
            var path = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Path;
            options.UseSqlite(ReviewDatabase.ConnectionString(path));
        });

        services.AddSingleton<DevOpsApiVersionCache>();
        services.AddHttpClient<IDevOpsClient, DevOpsClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
            {
                // On-prem Windows auth (NTLM/Kerberos) with the signed-in account instead of a PAT.
                UseDefaultCredentials = sp.GetRequiredService<IOptions<DevOpsOptions>>().Value.UseDefaultCredentials,
            });

        // Runs after options validation and before Kestrel listens: resolves repositories, migrates and seeds.
        services.AddHostedService<StartupInitializer>();

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(o => o.Path = PathExpansion.Expand(o.Path))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .PostConfigure(o => o.ExecutablePath = PathExpansion.Expand(o.ExecutablePath))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GitOptions>()
            .Bind(configuration.GetSection(GitOptions.SectionName))
            .PostConfigure(o => o.WorktreeRoot = PathExpansion.Expand(o.WorktreeRoot))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DevOpsOptions>()
            .Bind(configuration.GetSection(DevOpsOptions.SectionName))
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

        services.AddOptions<ReviewOptions>()
            .Bind(configuration.GetSection(ReviewOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TrayOptions>()
            .Bind(configuration.GetSection(TrayOptions.SectionName))
            .ValidateOnStart();

        // Checks beyond data annotations: files and directories exist, URL forms, per-item repository validation.
        services.AddSingleton<IValidateOptions<ClaudeOptions>, ClaudeOptionsValidator>();
        services.AddSingleton<IValidateOptions<DevOpsOptions>, DevOpsOptionsValidator>();
        services.AddSingleton<IValidateOptions<RepositoriesOptions>, RepositoriesOptionsValidator>();
    }
}
