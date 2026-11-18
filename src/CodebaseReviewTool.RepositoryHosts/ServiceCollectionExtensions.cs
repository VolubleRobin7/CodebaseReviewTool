using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.RepositoryHosts.AzureDevOps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.RepositoryHosts;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DevOps options, the DevOps HttpClient and <see cref="AzureDevOpsRepositoryHost"/> as the
    /// <see cref="IRepositoryHost"/> Application uses. The host is transient like the typed client it wraps, so callers
    /// that live long resolve it per use from a scope.
    /// </summary>
    public static IServiceCollection AddRepositoryHosts(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DevOpsOptions>()
            .Bind(configuration.GetSection(DevOpsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Checks that the organisation URL is in a recognised form, beyond the data annotations.
        services.AddSingleton<IValidateOptions<DevOpsOptions>, DevOpsOptionsValidator>();

        services.AddSingleton<DevOpsApiVersionCache>();
        services.AddHttpClient<IDevOpsClient, DevOpsClient>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
            {
                // On-prem Windows auth (NTLM/Kerberos) with the signed-in account instead of a PAT.
                UseDefaultCredentials = sp.GetRequiredService<IOptions<DevOpsOptions>>().Value.UseDefaultCredentials,
            });

        services.AddTransient<IRepositoryHost, AzureDevOpsRepositoryHost>();

        return services;
    }
}
