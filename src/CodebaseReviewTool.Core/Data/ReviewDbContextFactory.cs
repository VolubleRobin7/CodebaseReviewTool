using CodebaseReviewTool.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodebaseReviewTool.Core.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>, so migrations can be added and listed without building the Host (whose startup
/// validation needs the machine's real configuration). Points at a scratch database under the temp folder, never at
/// the installed copy's database, or at <c>REVIEWTOOL_DB_PATH</c> when set.
/// </summary>
public sealed class ReviewDbContextFactory : IDesignTimeDbContextFactory<ReviewDbContext>
{
    private const string DefaultPath = @"%TEMP%\CodebaseReviewTool\design-time.db";

    public ReviewDbContext CreateDbContext(string[] args)
    {
        var path = PathExpansion.Expand(Environment.GetEnvironmentVariable("REVIEWTOOL_DB_PATH") ?? DefaultPath);

        var options = new DbContextOptionsBuilder<ReviewDbContext>()
            .UseSqlite(ReviewDatabase.ConnectionString(path))
            .Options;

        return new ReviewDbContext(options);
    }
}
