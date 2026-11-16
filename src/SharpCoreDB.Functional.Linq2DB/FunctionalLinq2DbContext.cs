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
/// C# 15: Uses primary constructor for immutable dependency injection.
/// </summary>
/// <param name="connection">The underlying SharpCoreDB linq2db data connection</param>
public sealed class FunctionalLinq2DbContext(SharpCoreDBDataConnection connection)
{
    private readonly SharpCoreDBDataConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>
    /// Gets an entity by its mapped primary key.
    /// <para>
    /// Up to 2.1.4 this ignored <paramref name="keyValues"/> and returned the first row of the table, and it turned
    /// every exception into <c>None</c>. A query that fails now throws, as <see cref="FindOneAsync{T}"/> does.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="keyValues">
    /// One value per primary-key column, in <c>PrimaryKey(order)</c> order. A value of another numeric type (an
    /// <see cref="int"/> for a <see cref="long"/> key) is converted to the member's type.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Some entity when a row has that key; none otherwise.</returns>
    /// <exception cref="ArgumentException">
    /// The number of values differs from the number of key columns, or a value cannot be converted to its column's type.
    /// </exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has no mapped primary key.</exception>
    public async Task<Option<T>> GetByIdAsync<T>(
        object[] keyValues,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        cancellationToken.ThrowIfCancellationRequested();

        var predicate = BuildPrimaryKeyPredicate<T>(keyValues);
        return await FindOneAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds <c>row =&gt; row.Key1 == keyValues[0] &amp;&amp; …</c> from the entity's mapped primary-key columns.
    /// The values are read through a holder object, the same shape as a compiler-generated closure, so linq2db binds
    /// them as parameters and caches one query per entity type instead of one per key.
    /// </summary>
    private Expression<Func<T, bool>> BuildPrimaryKeyPredicate<T>(object[] keyValues)
        where T : class
    {
        var keyColumns = _connection.MappingSchema
            .GetEntityDescriptor(typeof(T), _connection.Options.ConnectionOptions.OnEntityDescriptorCreated)
            .Columns
            .Where(column => column.IsPrimaryKey)
            .OrderBy(column => column.PrimaryKeyOrder)
            .ToArray();

        if (keyColumns.Length == 0)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} has no mapped primary key; use FindOneAsync with an explicit predicate.");
        }

        if (keyValues.Length != keyColumns.Length)
        {
            throw new ArgumentException(
                $"{typeof(T).Name} has {keyColumns.Length} primary-key column(s) " +
                $"({string.Join(", ", keyColumns.Select(column => column.MemberName))}), " +
                $"but {keyValues.Length} key value(s) were supplied.",
                nameof(keyValues));
        }

        var holder = new KeyValueHolder(new object?[keyColumns.Length]);
        for (var i = 0; i < keyColumns.Length; i++)
        {
            holder.Values[i] = ConvertKeyValue(keyValues[i], keyColumns[i].MemberType, keyColumns[i].MemberName);
        }

        var row = Expression.Parameter(typeof(T), "row");
        var values = Expression.Field(Expression.Constant(holder), nameof(KeyValueHolder.Values));
        Expression? body = null;
        for (var i = 0; i < keyColumns.Length; i++)
        {
            var equal = Expression.Equal(
                Expression.MakeMemberAccess(row, keyColumns[i].MemberInfo),
                Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(i)), keyColumns[i].MemberType));
            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        return Expression.Lambda<Func<T, bool>>(body!, row);
    }

    private static object? ConvertKeyValue(object? value, Type memberType, string memberName)
    {
        var targetType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (value is null)
        {
            return !memberType.IsValueType || targetType != memberType
                ? null
                : throw new ArgumentException($"Key value for {memberName} cannot be null.", "keyValues");
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            return targetType.IsEnum
                ? Enum.ToObject(targetType, value)
                : Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException
                                       or ArgumentException)
        {
            throw new ArgumentException(
                $"Key value '{value}' ({value.GetType().Name}) cannot be converted to {targetType.Name} for {memberName}.",
                "keyValues",
                ex);
        }
    }

    private sealed class KeyValueHolder(object?[] values)
    {
        public readonly object?[] Values = values;
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
        var list = await q.Take(1).ToListAsync(cancellationToken).ConfigureAwait(false); // Explicit Take(1) + ToListAsync for optimal linq2db translation and C# 15 overload resolution
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
