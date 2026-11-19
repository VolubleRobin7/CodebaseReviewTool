using CodebaseReviewTool.Application.Agents;
using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Application.Configuration;
using CodebaseReviewTool.Application.Data;
using CodebaseReviewTool.Application.Repositories;
using CodebaseReviewTool.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseReviewTool.Host.Hosting;

/// <summary>
/// One-off startup work, run after options validation and before Kestrel starts listening: resolves the configured
/// repositories into <see cref="IRepositoryRegistry"/>, then migrates and seeds the database. A
/// failure throws (<see cref="StartupValidationException"/> for configuration problems), which stops the host; that
/// includes building <see cref="IAgentRegistry"/>, which happens here when it finds no agent or a duplicate key.
/// </summary>
public sealed class StartupInitializer(
    IAgentRegistry agents,
    RepositoryResolver resolver,
    RepositoryRegistry registry,
    IDbContextFactory<ReviewDbContext> dbContextFactory,
    IReviewDbContextFactory reviewDbContextFactory,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<StartupInitializer> logger) : IHostedService
{
    private readonly IAgentRegistry _agents = agents;
    private readonly RepositoryResolver _resolver = resolver;
    private readonly RepositoryRegistry _registry = registry;
    private readonly IDbContextFactory<ReviewDbContext> _dbContextFactory = dbContextFactory;
    private readonly IReviewDbContextFactory _reviewDbContextFactory = reviewDbContextFactory;
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

        foreach (var agent in _agents.Agents)
        {
            _logger.LogInformation(
                "Agent {Key}: {DisplayName}, models {Models}",
                agent.Key,
                agent.DisplayName,
                string.Join(", ", agent.Models.Select(m => m.Name)));
        }

        var repositories = await _resolver.ResolveAllAsync(cancellationToken);
        _registry.Set(repositories);
        foreach (var repository in repositories)
        {
            _logger.LogInformation(
                "Repository {Key}: {Organization}/{Project}/{Repository} via remote {Remote} in {LocalPath}",
                repository.Key, repository.Organization, repository.Project, repository.Repository, repository.RemoteName, repository.LocalPath);
        }

        await using (var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await ReviewDatabaseSeeder.SeedAsync(_reviewDbContextFactory, cancellationToken);
        _logger.LogInformation("Database ready at {Path}", _databaseOptions.Path);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
