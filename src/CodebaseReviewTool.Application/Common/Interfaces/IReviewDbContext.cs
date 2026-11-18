namespace CodebaseReviewTool.Application.Common.Interfaces;

/// <summary>
/// The review database, as Application sees it. Queries are plain LINQ over <see cref="GetEntities{TEntity}"/>;
/// changes are tracked and written by <see cref="SaveChangesAsync"/> in one transaction. A context is short-lived:
/// create one per operation with <see cref="IReviewDbContextFactory"/> and dispose it, because the Blazor circuits and
/// the background services run at the same time and must never share one.
/// </summary>
public interface IReviewDbContext : IDisposable, IAsyncDisposable
{
    void Add<TEntity>(TEntity entity) where TEntity : class;

    IQueryable<TEntity> GetEntities<TEntity>() where TEntity : class;

    void Remove<TEntity>(TEntity entity) where TEntity : class;

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
