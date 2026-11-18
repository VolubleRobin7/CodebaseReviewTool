using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseReviewTool.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    void IEntityTypeConfiguration<Review>.Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.Ignore(e => e.State);
        builder.Ignore(e => e.CurrentStep);
        // Reviews outlive their PR's open state; PR rows are never deleted while reviews exist. The foreign key gets its
        // own (PullRequestId, RepositoryKey) index by convention.
        builder.HasOne(e => e.PullRequest)
            .WithMany(p => p.Reviews)
            .HasForeignKey(e => new { e.PullRequestId, e.RepositoryKey })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
