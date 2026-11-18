using CodebaseReviewTool.Application.Configuration;
using CodebaseReviewTool.Application.Hosting;
using CodebaseReviewTool.Application.PullRequests;
using CodebaseReviewTool.Application.Reporting;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Application.Reviews;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseReviewTool.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the review services and their options. The hosted services that drive them are registered by Host,
    /// and ClaudeUsageMonitor's hosted-service registration with them, so the start order stays in one place.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReviewOptions>()
            .Bind(configuration.GetSection(ReviewOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TrayOptions>()
            .Bind(configuration.GetSection(TrayOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<RepositoryResolver>();
        services.AddSingleton<RepositoryRegistry>();
        services.AddSingleton<IRepositoryRegistry>(sp => sp.GetRequiredService<RepositoryRegistry>());

        services.AddSingleton<ReviewEvents>();
        services.AddSingleton<PullRequestCache>();
        services.AddSingleton<PollControl>();
        services.AddSingleton<IPollControl>(sp => sp.GetRequiredService<PollControl>());
        services.AddSingleton<ReviewQueue>();
        services.AddSingleton<ReviewRuns>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<ReviewStatusReader>();
        services.AddSingleton<IssuePostingService>();
        services.AddSingleton<ReviewOrchestrator>();
        services.AddSingleton<ReportDataLoader>();

        services.AddSingleton<ClaudeUsageMonitor>();
        services.AddSingleton<IClaudeUsage>(sp => sp.GetRequiredService<ClaudeUsageMonitor>());

        return services;
    }
}
