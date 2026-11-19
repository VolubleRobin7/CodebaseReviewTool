using CodebaseReviewTool.Agents.Claude;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Infrastructure.Configuration;
using CodebaseReviewTool.Infrastructure.Processes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Agents;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IAgent"/> for every AI tool that has a section in configuration; Application's
    /// agent registry collects them. A tool without a section is left out, and one with a section is validated at
    /// startup, so a misconfigured tool stops the app.
    /// </summary>
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Git registers the same runner; whichever is first wins.
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();

        // Another tool means another block like this one; the first registered is the default for new steps.
        if (configuration.GetSection(ClaudeOptions.SectionName).Exists())
        {
            services.AddOptions<ClaudeOptions>()
                .Bind(configuration.GetSection(ClaudeOptions.SectionName))
                .PostConfigure(o => o.ExecutablePath = PathExpansion.Expand(o.ExecutablePath))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Checks that the executable exists, beyond the data annotations.
            services.AddSingleton<IValidateOptions<ClaudeOptions>, ClaudeOptionsValidator>();

            services.AddSingleton<IAgent, ClaudeCodeAgent>();
        }

        return services;
    }
}
