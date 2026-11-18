using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseReviewTool.Persistence.Configurations;

public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    void IEntityTypeConfiguration<Issue>.Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("Issues");
        builder.HasIndex(e => e.ReviewId);
        builder.HasOne(e => e.Review)
            .WithMany(r => r.Issues)
            .HasForeignKey(e => e.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
