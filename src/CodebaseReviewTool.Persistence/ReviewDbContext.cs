using CodebaseReviewTool.Application.Common.Interfaces;
using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodebaseReviewTool.Persistence;

/// <summary>
/// SQLite storage for the tool. Application reaches it as <see cref="IReviewDbContext"/>, created per operation by
/// <see cref="IReviewDbContextFactory"/>; the table mappings are the <c>Configurations</c> classes.
/// </summary>
public class ReviewDbContext(DbContextOptions<ReviewDbContext> options) : DbContext(options), IReviewDbContext
{
    public DbSet<FlowStep> FlowSteps => Set<FlowStep>();

    public DbSet<PullRequest> PullRequests => Set<PullRequest>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ReviewStepRun> ReviewStepRuns => Set<ReviewStepRun>();

    public DbSet<Issue> Issues => Set<Issue>();

    void IReviewDbContext.Add<TEntity>(TEntity entity) => Set<TEntity>().Add(entity);

    IQueryable<TEntity> IReviewDbContext.GetEntities<TEntity>() => Set<TEntity>();

    void IReviewDbContext.Remove<TEntity>(TEntity entity) => Set<TEntity>().Remove(entity);

    Task IReviewDbContext.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums use EF's default integer storage. Every DateTime is UTC and comes back with Kind = Utc.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewDbContext).Assembly);

    // Create a migration (from the repository root):
    // dotnet ef migrations add <Name> --project src/CodebaseReviewTool.Persistence --startup-project src/CodebaseReviewTool.Persistence --output-dir Migrations
    //
    // Pending migrations are applied when the app starts (StartupInitializer in Host).

    internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
