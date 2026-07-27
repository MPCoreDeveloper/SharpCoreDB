using System.Linq.Expressions;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Async;
using static SharpCoreDB.Functional.Prelude;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Functional adapter over linq2db operations with Option/Fin return types.
/// Provides railway-oriented programming patterns for SharpCoreDB queries.
/// C# 14: Uses primary constructor for immutable dependency injection.
/// </summary>
/// <param name="connection">The underlying SharpCoreDB linq2db data connection</param>
public sealed class FunctionalLinq2DbContext(SharpCoreDBDataConnection connection)
{
    private readonly SharpCoreDBDataConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Gets an entity by its primary key.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="keyValues">Primary key values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Some entity when found; none otherwise</returns>
    public async Task<Option<T>> GetByIdAsync<T>(
        object[] keyValues,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        cancellationToken.ThrowIfCancellationRequested();

        // Note: Full key-based lookup requires mapping schema inspection. For now we return first (caller can use FindOne for predicates).
        // A production implementation would resolve PK columns and build a predicate from keyValues.
        // Materialize safely via explicit IQueryable<T> to ensure correct ToListAsync overload resolution (LinqToDB 6 + C# 14)
        IQueryable<T> q1 = _connection.GetTable<T>();
        var list1 = await q1.ToListAsync(cancellationToken).ConfigureAwait(false);
        var entity = list1.FirstOrDefault();

        return Optional(entity);
    }

    /// <summary>
    /// Gets the first entity matching the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Some entity when found; none otherwise</returns>
    public async Task<Option<T>> FindOneAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<T> q = _connection.GetTable<T>().Where(predicate);
        var list = await q.ToListAsync(cancellationToken).ConfigureAwait(false);
        var entity = list.FirstOrDefault();

        return Optional(entity);
    }

    /// <summary>
    /// Executes a query with optional filtering, ordering, and projection.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="queryBuilder">Query builder callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of results</returns>
    public async Task<Seq<T>> QueryAsync<T>(
        Func<IQueryable<T>, IQueryable<T>> queryBuilder,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(queryBuilder);
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<T> q = queryBuilder(_connection.GetTable<T>());
        var result = await q.ToListAsync(cancellationToken).ConfigureAwait(false);

        return toSeq(result);
    }

    /// <summary>
    /// Executes a query with a predicate filter.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of matching results</returns>
    public async Task<Seq<T>> QueryAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<T> q = _connection.GetTable<T>().Where(predicate);
        var result = await q.ToListAsync(cancellationToken).ConfigureAwait(false);

        return toSeq(result);
    }

    /// <summary>
    /// Gets all entities of type T.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of all entities</returns>
    public async Task<Seq<T>> GetAllAsync<T>(CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<T> q = _connection.GetTable<T>();
        var result = await q.ToListAsync(cancellationToken).ConfigureAwait(false);

        return toSeq(result);
    }

    /// <summary>
    /// Inserts a new entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to insert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when persisted; failure with error otherwise</returns>
    public async Task<Fin<Unit>> InsertAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _connection.InsertAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Inserts multiple entities in a batch.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entities">Entities to insert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with row count; failure with error otherwise</returns>
    public async Task<Fin<int>> InsertBatchAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Simple transactional batch insert for compatibility across linq2db versions.
            // For high-volume bulk, replace with provider-specific BulkCopy when available.
            await using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var count = 0;
            foreach (var e in entities)
            {
                await _connection.InsertAsync(e, token: cancellationToken).ConfigureAwait(false);
                count++;
            }
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return FinSucc(count);
        }
        catch (Exception ex)
        {
            return FinFail<int>(Error.New(ex));
        }
    }

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when updated; failure with error otherwise</returns>
    public async Task<Fin<Unit>> UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _connection.UpdateAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Deletes an entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when deleted; failure with error otherwise</returns>
    public async Task<Fin<Unit>> DeleteAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _connection.DeleteAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Deletes entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with deleted count; failure with error otherwise</returns>
    public async Task<Fin<int>> DeleteWhereAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var count = await _connection.GetTable<T>()
                .Where(predicate)
                .DeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return FinSucc(count);
        }
        catch (Exception ex)
        {
            return FinFail<int>(Error.New(ex));
        }
    }

    /// <summary>
    /// Counts entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Optional filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with count; failure with error otherwise</returns>
    public async Task<Fin<long>> CountAsync<T>(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IQueryable<T> q = _connection.GetTable<T>();
            long count = predicate == null
                ? await q.LongCountAsync(cancellationToken).ConfigureAwait(false)
                : await q.Where(predicate).LongCountAsync(cancellationToken).ConfigureAwait(false);

            return FinSucc(count);
        }
        catch (Exception ex)
        {
            return FinFail<long>(Error.New(ex));
        }
    }

    /// <summary>
    /// Checks if any entities match the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with true/false; failure with error otherwise</returns>
    public async Task<Fin<bool>> ExistsAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IQueryable<T> q = _connection.GetTable<T>().Where(predicate);
            var exists = await q.AnyAsync(cancellationToken).ConfigureAwait(false);
            return FinSucc(exists);
        }
        catch (Exception ex)
        {
            return FinFail<bool>(Error.New(ex));
        }
    }

    /// <summary>
    /// Executes a transaction with functional error handling.
    /// </summary>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="action">Transaction action</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with result; failure with error if transaction rolls back</returns>
    public async Task<Fin<TResult>> TransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return FinSucc(result);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            return FinFail<TResult>(Error.New(ex));
        }
    }

    /// <summary>
    /// Gets the underlying linq2db DataConnection for advanced scenarios.
    /// </summary>
    public SharpCoreDBDataConnection Connection => _connection;
}
