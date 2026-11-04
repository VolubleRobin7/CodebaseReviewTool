using CodebaseReviewTool.Core.Configuration;
using CodebaseReviewTool.Core.Data;
using CodebaseReviewTool.Core.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Core.Hosting;

/// <summary>
/// One-off startup work, run after options validation and before Kestrel starts listening: resolves the configured
/// repositories into <see cref="IRepositoryRegistry"/>, then migrates and seeds the database. A failure throws
/// (<see cref="StartupValidationException"/> for configuration problems), which stops the host.
/// </summary>
public sealed class StartupInitializer(
    RepositoryResolver resolver,
    RepositoryRegistry registry,
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<StartupInitializer> logger) : IHostedService
{
    private readonly RepositoryResolver _resolver = resolver;
    private readonly RepositoryRegistry _registry = registry;
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;
    private readonly ILogger<StartupInitializer> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (EF.IsDesignTime)
        {
            // The dotnet-ef tools build and briefly start the host to find the DbContext. Migrating here with the
            // migrations folder empty would create the schema from the model with no migration history.
            return;
        }

        var repositories = await _resolver.ResolveAllAsync(cancellationToken);
        _registry.Set(repositories);
        foreach (var repository in repositories)
        {
            _logger.LogInformation(
                "Repository {Key}: {Organization}/{Project}/{Repository} via remote {Remote} in {LocalPath}",
                repository.Key, repository.Organization, repository.Project, repository.Repository, repository.RemoteName, repository.LocalPath);
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await ReviewDatabase.MigrateAndSeedAsync(db, cancellationToken);
        _logger.LogInformation("Database ready at {Path}", _databaseOptions.Path);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
