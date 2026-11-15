using CodebaseReviewTool.Core.Agents;
using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.DevOps;
using CodebaseReviewTool.Core.Git;
using CodebaseReviewTool.Core.Hosting;
using CodebaseReviewTool.Core.Processes;
using CodebaseReviewTool.Core.Reporting;
using CodebaseReviewTool.Core.Reviews;
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
        services.AddSingleton<WorktreeService>();

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

        services.AddSingleton<ReviewEvents>();
        services.AddSingleton<PullRequestCache>();
        services.AddSingleton<PollControl>();
        services.AddSingleton<IPollControl>(sp => sp.GetRequiredService<PollControl>());
        services.AddSingleton<ReviewQueue>();
        services.AddSingleton<ReviewRuns>();
        services.AddSingleton<ReviewService>();

        // The AI tool that runs each flow step; another tool means another IAgentRunner registered here.
        services.AddSingleton<IAgentRunner, ClaudeCodeRunner>();
        services.AddSingleton<ReviewOrchestrator>();
        services.AddSingleton<ReportDataLoader>();

        // Hosted services start in registration order. The initializer runs after options validation and before
        // Kestrel listens (resolves repositories, migrates and seeds), so the services registered after it always
        // find the registry filled and the schema current. The queue recovers the previous run's reviews in its own
        // start, also before Kestrel listens.
        services.AddHostedService<StartupInitializer>();
        services.AddHostedService<DevOpsPollService>();
        services.AddHostedService<ReviewQueueService>();
        services.AddHostedService<WorktreeCleanupService>();

        services.AddSingleton<ClaudeUsageMonitor>();
        services.AddSingleton<IClaudeUsage>(sp => sp.GetRequiredService<ClaudeUsageMonitor>());
        services.AddHostedService(sp => sp.GetRequiredService<ClaudeUsageMonitor>());

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
