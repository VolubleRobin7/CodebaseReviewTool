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
    /// Registers the Claude options and the <see cref="IAgentRunner"/> that runs each flow step and reads the plan's usage.
    /// </summary>
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ClaudeOptions>()
            .Bind(configuration.GetSection(ClaudeOptions.SectionName))
            .PostConfigure(o => o.ExecutablePath = PathExpansion.Expand(o.ExecutablePath))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Checks that the executable exists, beyond the data annotations.
        services.AddSingleton<IValidateOptions<ClaudeOptions>, ClaudeOptionsValidator>();

        // Git registers the same runner; whichever is first wins.
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();

        // The AI tool that runs each flow step; another tool means another IAgentRunner registered here.
        services.AddSingleton<IAgentRunner, ClaudeCodeRunner>();

        return services;
    }
}
