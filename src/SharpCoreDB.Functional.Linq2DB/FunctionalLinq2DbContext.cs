using System.Linq.Expressions;
using System.Runtime.InteropServices;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using static SharpCoreDB.Functional.Prelude;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Functional adapter over linq2db operations with Option/Fin return types.
/// Provides railway-oriented programming patterns for SharpCoreDB queries.
/// C# 15: Uses primary constructor for immutable dependency injection.
/// <para>
/// The error contract: methods returning <see cref="Option{T}"/> or <see cref="Seq{T}"/> throw when the query fails;
/// methods returning <see cref="Fin{T}"/> capture the failure as <c>Fail</c>; and cancellation always surfaces as
/// <see cref="OperationCanceledException"/> — before the call or during it — never as a failure.
/// </para>
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

        // Called through linq2db's own extension class: since .NET 10 the BCL's System.Linq.AsyncEnumerable offers a
        // FirstOrDefaultAsync for the same receiver, and that ambiguity is why this used Take(1).ToListAsync(), which
        // allocated a list for one row.
        var entity = await AsyncExtensions.FirstOrDefaultAsync(_connection.GetTable<T>().Where(predicate), cancellationToken)
            .ConfigureAwait(false);

        return Optional(entity);
    }

    /// <summary>
    /// Executes a query with optional filtering, ordering, and projection.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="queryBuilder">Query builder callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of results</returns>
    public Task<Seq<T>> QueryAsync<T>(
        Func<IQueryable<T>, IQueryable<T>> queryBuilder,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(queryBuilder);
        cancellationToken.ThrowIfCancellationRequested();

        return ToSeqAsync(queryBuilder(_connection.GetTable<T>()), cancellationToken);
    }

    /// <summary>
    /// Executes a query with a predicate filter.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of matching results</returns>
    public Task<Seq<T>> QueryAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        return ToSeqAsync(_connection.GetTable<T>().Where(predicate), cancellationToken);
    }

    /// <summary>
    /// Gets all entities of type T.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A functional sequence of all entities</returns>
    public Task<Seq<T>> GetAllAsync<T>(CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ToSeqAsync(_connection.GetTable<T>(), cancellationToken);
    }

    /// <summary>
    /// Inserts a new entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to insert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when persisted; failure with error otherwise</returns>
    public Task<Fin<Unit>> InsertAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(async () =>
        {
            await _connection.InsertAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return unit;
        });
    }

    /// <summary>
    /// Inserts multiple entities in a batch using linq2db BulkCopy (multi-row SQL, up to 1000 rows per statement).
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entities">Entities to insert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with row count; failure with error otherwise</returns>
    public Task<Fin<int>> InsertBatchAsync<T>(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default)
        where T : class
        => InsertBatchAsync(entities, DefaultBulkCopyOptions, cancellationToken);

    /// <summary>
    /// Inserts multiple entities in a batch using linq2db BulkCopy with the caller's options — batch size,
    /// parameters, row-copied notifications.
    /// <para>
    /// <see cref="BulkCopyType.ProviderSpecific"/> is replaced by <see cref="BulkCopyType.MultipleRows"/>: the
    /// provider-specific path needs a native Microsoft.Data.Sqlite connection, and the adapter's connection is
    /// SharpCoreDB's. The caller's <paramref name="options"/> instance is not modified.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entities">Entities to insert</param>
    /// <param name="options">Bulk copy options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Success with the row count; failure with error otherwise, including a count above <see cref="int.MaxValue"/>
    /// (up to 2.1.4 the count was cast and wrapped to a negative number).
    /// </returns>
    public async Task<Fin<int>> InsertBatchAsync<T>(
        IEnumerable<T> entities,
        BulkCopyOptions options,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var effective = options.BulkCopyType == BulkCopyType.ProviderSpecific
            ? options with { BulkCopyType = BulkCopyType.MultipleRows }
            : options;

        var copied = await RunAsync(async () =>
            (await _connection.BulkCopyAsync(effective, entities, cancellationToken).ConfigureAwait(false)).RowsCopied)
            .ConfigureAwait(false);

        return copied.Bind(ToRowCount);
    }

    /// <summary>
    /// Converts a bulk-copy row count to the <see cref="int"/> the batch API returns: a count that does not fit is a
    /// failure, not a wrapped negative number.
    /// </summary>
    /// <param name="rowsCopied">The row count linq2db reported.</param>
    /// <returns>The count, or a failure carrying an <see cref="OverflowException"/>.</returns>
    internal static Fin<int> ToRowCount(long rowsCopied) =>
        rowsCopied <= int.MaxValue
            ? FinSucc((int)rowsCopied)
            : FinFail<int>(Error.New(new OverflowException(
                $"{rowsCopied} rows were copied, more than an Int32 count can report; split the batch.")));

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when updated; failure with error otherwise</returns>
    public Task<Fin<Unit>> UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(async () =>
        {
            await _connection.UpdateAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return unit;
        });
    }

    /// <summary>
    /// Deletes an entity.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="entity">Entity to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success when deleted; failure with error otherwise</returns>
    public Task<Fin<Unit>> DeleteAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(async () =>
        {
            await _connection.DeleteAsync(entity, token: cancellationToken).ConfigureAwait(false);
            return unit;
        });
    }

    /// <summary>
    /// Deletes entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with deleted count; failure with error otherwise</returns>
    public Task<Fin<int>> DeleteWhereAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(() => _connection.GetTable<T>().Where(predicate).DeleteAsync(cancellationToken));
    }

    /// <summary>
    /// Counts entities matching the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Optional filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with count; failure with error otherwise</returns>
    public Task<Fin<long>> CountAsync<T>(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<T> q = _connection.GetTable<T>();
        if (predicate is not null)
        {
            q = q.Where(predicate);
        }

        return RunAsync(() => q.LongCountAsync(cancellationToken));
    }

    /// <summary>
    /// Checks if any entities match the predicate.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="predicate">Filter predicate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with true/false; failure with error otherwise</returns>
    public Task<Fin<bool>> ExistsAsync<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();

        return RunAsync(() => _connection.GetTable<T>().Where(predicate).AnyAsync(cancellationToken));
    }

    /// <summary>
    /// Executes <paramref name="action"/> in a transaction: committed when it completes, rolled back when it throws.
    /// Prefer the overload that hands the action this context and the token.
    /// </summary>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="action">Transaction action</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with result; failure with the action's error if the transaction rolls back</returns>
    public Task<Fin<TResult>> TransactionAsync<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return TransactionAsync((_, _) => action(), cancellationToken);
    }

    /// <summary>
    /// Executes <paramref name="action"/> in a transaction, passing it this context and the token so the work inside
    /// is visibly bound to the transaction's connection: committed when it completes, rolled back when it throws.
    /// <para>
    /// The rollback runs with <see cref="CancellationToken.None"/>. Up to 2.1.4 it used the caller's token: once that
    /// token was cancelled the rollback itself threw, and the action's own error was replaced by "The operation was
    /// canceled". Cancellation surfaces as <see cref="OperationCanceledException"/>, never as a failure.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">Result type</typeparam>
    /// <param name="action">Transaction action, given this context and <paramref name="cancellationToken"/>.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success with result; failure with the action's error if the transaction rolls back</returns>
    public async Task<Fin<TResult>> TransactionAsync<TResult>(
        Func<FunctionalLinq2DbContext, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        return await RunAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await action(this, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the underlying linq2db DataConnection for advanced scenarios.
    /// </summary>
    public SharpCoreDBDataConnection Connection => _connection;

    private static readonly BulkCopyOptions DefaultBulkCopyOptions = new()
    {
        MaxBatchSize = 1000, // Tuned for typical SharpCoreDB workloads
        UseParameters = true,
    };

    /// <summary>
    /// The <c>Fin</c> contract in one place: a failure becomes <c>Fail</c>, and cancellation propagates as
    /// <see cref="OperationCanceledException"/>. Up to 2.1.4 every method caught everything, so a token cancelled
    /// during the call came back as a failure while one cancelled before it threw.
    /// </summary>
    private static async Task<Fin<TResult>> RunAsync<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return FinSucc(await operation().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FinFail<TResult>(Error.New(ex));
        }
    }

    /// <summary>
    /// Materialises a query straight into the array a <see cref="Seq{T}"/> wraps. Up to 2.1.4 the rows were read into
    /// a list and then copied again by <c>toSeq</c>.
    /// </summary>
    private static async Task<Seq<T>> ToSeqAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        var rows = await AsyncExtensions.ToArrayAsync(query, cancellationToken).ConfigureAwait(false);
        return new Seq<T>(ImmutableCollectionsMarshal.AsImmutableArray(rows));
    }
}
