using CodebaseReviewTool.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the database options and the <see cref="ReviewDbContext"/> factory.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(o => o.Path = PathExpansion.Expand(o.Path))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContextFactory<ReviewDbContext>((sp, options) =>
        {
            var path = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Path;
            options.UseSqlite(ReviewDatabase.ConnectionString(path));
        });

        return services;
    }
}
