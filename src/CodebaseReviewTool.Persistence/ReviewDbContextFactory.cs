using CodebaseReviewTool.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CodebaseReviewTool.Persistence;

/// <summary>Hands Application a new <see cref="ReviewDbContext"/> from EF Core's pooled-options factory.</summary>
public sealed class ReviewDbContextFactory(IDbContextFactory<ReviewDbContext> dbContextFactory) : IReviewDbContextFactory
{
    IReviewDbContext IReviewDbContextFactory.CreateDbContext() => dbContextFactory.CreateDbContext();
}
