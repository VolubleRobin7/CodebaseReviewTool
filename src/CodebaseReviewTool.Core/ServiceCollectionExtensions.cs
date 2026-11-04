using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Phase 0: nothing registered yet. Phase 1 adds the options classes and the DbContext factory;
        // later phases add the DevOps client, git/Claude runners, orchestrator and hosted services.
        return services;
    }
}
