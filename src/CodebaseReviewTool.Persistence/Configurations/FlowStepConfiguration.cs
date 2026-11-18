using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseReviewTool.Persistence.Configurations;

public class FlowStepConfiguration : IEntityTypeConfiguration<FlowStep>
{
    void IEntityTypeConfiguration<FlowStep>.Configure(EntityTypeBuilder<FlowStep> builder)
    {
        builder.ToTable("FlowSteps");
        builder.HasKey(e => e.Step);
        builder.Property(e => e.Step).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(100);
    }
}
