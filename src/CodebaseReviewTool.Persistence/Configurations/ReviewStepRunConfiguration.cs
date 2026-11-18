using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseReviewTool.Persistence.Configurations;

public class ReviewStepRunConfiguration : IEntityTypeConfiguration<ReviewStepRun>
{
    void IEntityTypeConfiguration<ReviewStepRun>.Configure(EntityTypeBuilder<ReviewStepRun> builder)
    {
        builder.ToTable("ReviewStepRuns");
        builder.Ignore(e => e.State);
        builder.HasIndex(e => e.ReviewId);
        builder.HasOne(e => e.Review)
            .WithMany(r => r.StepRuns)
            .HasForeignKey(e => e.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
