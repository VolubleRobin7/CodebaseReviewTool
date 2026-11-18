using CodebaseReviewTool.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodebaseReviewTool.Persistence.Configurations;

public class PullRequestConfiguration : IEntityTypeConfiguration<PullRequest>
{
    void IEntityTypeConfiguration<PullRequest>.Configure(EntityTypeBuilder<PullRequest> builder)
    {
        builder.ToTable("PullRequests");
        builder.HasKey(e => new { e.Id, e.RepositoryKey });
        builder.Property(e => e.Id).ValueGeneratedNever();
        // The poller marks a repository's open rows closed; the home page lists open rows.
        builder.HasIndex(e => new { e.RepositoryKey, e.IsOpen });
    }
}
