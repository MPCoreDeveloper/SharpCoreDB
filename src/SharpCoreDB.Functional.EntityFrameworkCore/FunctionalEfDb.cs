namespace SharpCoreDB.Functional.EntityFrameworkCore;

using System.Linq.Expressions;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.EntityFrameworkCore;
using static LanguageExt.Prelude;

/// <summary>
/// Functional adapter over EF Core operations with Option/Fin return types.
/// </summary>
/// <param name="context">The underlying DbContext instance.</param>
public sealed class FunctionalEfDb(DbContext context)
{
    private readonly DbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Gets an entity by its primary key.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="keyValues">Primary key values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Some entity when found; none otherwise.</returns>
    public async Task<Option<TEntity>> GetByIdAsync<TEntity>(
        object[] keyValues,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await _context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);
        return Optional(entity);
    }

    /// <summary>
    /// Gets the first entity matching the predicate.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="predicate">Filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Some entity when found; none otherwise.</returns>
    public async Task<Option<TEntity>> FindOneAsync<TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await _context.Set<TEntity>().FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
        return Optional(entity);
    }

    /// <summary>
    /// Runs a functional query projection over an entity set.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="queryBuilder">Query builder callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A sequence with the query results.</returns>
    public async Task<Seq<TEntity>> QueryAsync<TEntity>(
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(queryBuilder);
        cancellationToken.ThrowIfCancellationRequested();

        var query = queryBuilder(_context.Set<TEntity>().AsQueryable());
        var result = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return toSeq(result);
    }

    /// <summary>
    /// Adds and saves a new entity.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="entity">Entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when persisted; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> InsertAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _context.Set<TEntity>().Add(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Updates and saves an entity.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="entity">Entity to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when persisted; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _context.Set<TEntity>().Update(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Removes and saves an entity.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="entity">Entity to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when persisted; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _context.Set<TEntity>().Remove(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Counts entities with an optional predicate.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <param name="predicate">Optional filter predicate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count value.</returns>
    public Task<long> CountAsync<TEntity>(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        where TEntity : class =>
        predicate is null
            ? _context.Set<TEntity>().LongCountAsync(cancellationToken)
            : _context.Set<TEntity>().LongCountAsync(predicate, cancellationToken);
}
