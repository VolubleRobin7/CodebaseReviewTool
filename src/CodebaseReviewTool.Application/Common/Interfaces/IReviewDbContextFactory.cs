namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>Creates a short-lived <see cref="IReviewDbContext"/>; the caller disposes it.</summary>
public interface IReviewDbContextFactory
{
    IReviewDbContext CreateDbContext();
}
