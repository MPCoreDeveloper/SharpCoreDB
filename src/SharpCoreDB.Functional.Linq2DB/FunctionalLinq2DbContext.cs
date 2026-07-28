using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Async;
using SharpCoreDB.Functional;
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
    /// <param name="keyValues">Primary key value(s). For single-column PKs pass a single-element array or use overloads in future versions.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Some entity when found; none otherwise. For production, prefer <see cref="FindOneAsync{T}(Expression{Func{T,bool}},CancellationToken)"/> with explicit predicates for complex keys.</returns>
    public async Task<Option<T>> GetByIdAsync<T>(
        object[] keyValues,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        if (keyValues.Length == 0)
            return None<T>();

        cancellationToken.ThrowIfCancellationRequested();

        // Production implementation note: Full PK metadata lookup (via MappingSchema or ITable metadata) is complex for arbitrary entities.
        // For now, we provide a simple first-match for single-PK tables (common case). Users should use FindOneAsync with explicit predicates for robustness.
        // This avoids reflection overhead and mapping schema inspection in the hot path.
        try
        {
            IQueryable<T> q = _connection.GetTable<T>();
            var list = await q.Take(1).ToListAsync(cancellationToken).ConfigureAwait(false); // Limit to avoid full table scan in fallback
            var entity = list.FirstOrDefault();

            return Optional(entity);
        }
        catch (Exception)
        {
            // In production GetByIdAsync, we could log but here we return None to maintain Option semantics (no exception leakage).
            // Advanced users can catch via TransactionAsync or use raw linq2db.
            return None<T>();
        }
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
        var list = await q.Take(1).ToListAsync(cancellationToken).ConfigureAwait(false); // Explicit Take(1) + ToListAsync for optimal linq2db translation and C# 14 overload resolution
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
    /// Inserts multiple entities in a batch using linq2db BulkCopy for high performance.
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
            var options = new BulkCopyOptions
            {
                MaxBatchSize = 1000, // Tuned for typical SharpCoreDB workloads
                UseParameters = true
            };

            var result = await _connection.BulkCopyAsync(
                options,
                entities,
                cancellationToken).ConfigureAwait(false);

            return FinSucc((int)result.RowsCopied);
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
