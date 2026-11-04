using CodebaseReviewTool.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodebaseReviewTool.Core.Data;

/// <summary>
/// SQLite storage for the tool. Consumers inject <see cref="IDbContextFactory{TContext}"/> and create a short-lived
/// context per operation; Blazor Server circuits and background services must not share one.
/// </summary>
public class ReviewDbContext(DbContextOptions<ReviewDbContext> options) : DbContext(options)
{
    public DbSet<FlowStep> FlowSteps => Set<FlowStep>();

    public DbSet<PullRequest> PullRequests => Set<PullRequest>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ReviewStepRun> ReviewStepRuns => Set<ReviewStepRun>();

    public DbSet<Issue> Issues => Set<Issue>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums use EF's default integer storage. Every DateTime is UTC and comes back with Kind = Utc.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FlowStep>(entity =>
        {
            entity.ToTable("FlowSteps");
            entity.HasKey(e => e.Step);
            entity.Property(e => e.Step).ValueGeneratedNever();
        });

        modelBuilder.Entity<PullRequest>(entity =>
        {
            entity.ToTable("PullRequests");
            entity.HasKey(e => new { e.Id, e.RepositoryKey });
            entity.Property(e => e.Id).ValueGeneratedNever();
            // The poller marks a repository's open rows closed; the home page lists open rows.
            entity.HasIndex(e => new { e.RepositoryKey, e.IsOpen });
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Reviews");
            entity.Ignore(e => e.State);
            entity.Ignore(e => e.CurrentStep);
            // Reviews outlive their PR's open state; PR rows are never deleted while reviews exist. The foreign key
            // gets its own (PullRequestId, RepositoryKey) index by convention.
            entity.HasOne(e => e.PullRequest)
                .WithMany(p => p.Reviews)
                .HasForeignKey(e => new { e.PullRequestId, e.RepositoryKey })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReviewStepRun>(entity =>
        {
            entity.ToTable("ReviewStepRuns");
            entity.Ignore(e => e.State);
            entity.HasIndex(e => e.ReviewId);
            entity.HasOne(e => e.Review)
                .WithMany(r => r.StepRuns)
                .HasForeignKey(e => e.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Issue>(entity =>
        {
            entity.ToTable("Issues");
            entity.HasIndex(e => e.ReviewId);
            entity.HasOne(e => e.Review)
                .WithMany(r => r.Issues)
                .HasForeignKey(e => e.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
