namespace SharpCoreDB.Functional;

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using LanguageExt;
using SharpCoreDB.Interfaces;
using static LanguageExt.Prelude;

/// <summary>
/// Functional wrapper over SharpCoreDB with Option/Fin-first APIs.
/// </summary>
/// <param name="inner">Inner database instance.</param>
/// <remarks>
/// Example fluent chain:
/// <code>
/// var result = await db.Functional()
///     .GetByIdAsync&lt;User&gt;("Users", 42, cancellationToken: ct)
///     .Map(opt => opt.Map(user => user.Name))
///     .Map(opt => opt.IfNone("unknown"));
/// </code>
/// </remarks>
public sealed class FunctionalDb(IDatabase inner)
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
    private readonly IDatabase _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Gets the default id column used by helper CRUD methods.
    /// </summary>
    public string DefaultIdColumn
    {
        get;
        init
        {
            field = string.IsNullOrWhiteSpace(value)
                ? "Id"
                : value;
        }
    } = "Id";

    /// <summary>
    /// Gets a single entity by id from a table.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="tableName">Source table name.</param>
    /// <param name="id">Identifier value.</param>
    /// <param name="idColumn">Id column name. Defaults to <see cref="DefaultIdColumn"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="Option{A}"/> with entity when found; none when not found.</returns>
    public Task<Option<T>> GetByIdAsync<T>(
        string tableName,
        object id,
        string? idColumn = null,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(id);

        cancellationToken.ThrowIfCancellationRequested();

        var key = string.IsNullOrWhiteSpace(idColumn) ? DefaultIdColumn : idColumn;
        var sql = $"SELECT * FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(key)} = @Id LIMIT 1";
        var rows = _inner.ExecuteQuery(sql, new Dictionary<string, object?> { ["Id"] = id });

        if (rows.Count == 0)
        {
            return Task.FromResult(Option<T>.None);
        }

        var mapped = TryMapRow<T>(rows[0]);
        return Task.FromResult(mapped);
    }

    /// <summary>
    /// Finds a single entity using a query expected to return at most one row.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="sql">Query SQL.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="Option{A}"/> with first entity, or none.</returns>
    public Task<Option<T>> FindOneAsync<T>(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _inner.ExecuteQuery(sql, parameters);
        if (rows.Count == 0)
        {
            return Task.FromResult(Option<T>.None);
        }

        return Task.FromResult(TryMapRow<T>(rows[0]));
    }

    /// <summary>
    /// Executes a query and projects rows into a functional sequence.
    /// </summary>
    /// <typeparam name="T">Row model type.</typeparam>
    /// <param name="sql">SQL query.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="LanguageExt.Seq{A}"/> of mapped rows.</returns>
    public Task<Seq<T>> QueryAsync<T>(
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _inner.ExecuteQuery(sql, parameters);
        if (rows.Count == 0)
        {
            return Task.FromResult(LanguageExt.Seq<T>.Empty);
        }

        var list = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var mapped = TryMapRow<T>(row);
            mapped.IfSome(list.Add);
        }

        return Task.FromResult(toSeq(list));
    }

    /// <summary>
    /// Inserts an entity into a table.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="tableName">Target table name.</param>
    /// <param name="entity">Entity to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure with wrapped error.</returns>
    public async Task<Fin<Unit>> InsertAsync<T>(
        string tableName,
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(entity);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var parameters = BuildEntityParameterMap(entity);
            if (parameters.Count == 0)
            {
                return ErrorHandling.Fail<Unit>(new InvalidOperationException("No writable properties found for insert."));
            }

            var columns = string.Join(", ", parameters.Keys.Select(QuoteIdentifier));
            var values = string.Join(", ", parameters.Keys.Select(name => $"@{name}"));
            var sql = $"INSERT INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({values})";

            await _inner.ExecuteSQLAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            return ErrorHandling.SuccessUnit();
        }
        catch (Exception ex)
        {
            return ErrorHandling.Fail<Unit>(ex);
        }
    }

    /// <summary>
    /// Updates an entity by id.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="tableName">Target table name.</param>
    /// <param name="id">Identifier value.</param>
    /// <param name="entity">Entity values to update.</param>
    /// <param name="idColumn">Id column name. Defaults to <see cref="DefaultIdColumn"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure with wrapped error.</returns>
    public async Task<Fin<Unit>> UpdateAsync<T>(
        string tableName,
        object id,
        T entity,
        string? idColumn = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(entity);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var key = string.IsNullOrWhiteSpace(idColumn) ? DefaultIdColumn : idColumn;
            var parameters = BuildEntityParameterMap(entity);
            parameters["_id"] = id;

            var setColumns = parameters.Keys
                .Where(name => !string.Equals(name, key, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "_id", StringComparison.Ordinal))
                .Select(name => $"{QuoteIdentifier(name)} = @{name}")
                .ToArray();

            if (setColumns.Length == 0)
            {
                return ErrorHandling.Fail<Unit>(new InvalidOperationException("No writable properties found for update."));
            }

            var sql = $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", setColumns)} WHERE {QuoteIdentifier(key)} = @_id";
            await _inner.ExecuteSQLAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            return ErrorHandling.SuccessUnit();
        }
        catch (Exception ex)
        {
            return ErrorHandling.Fail<Unit>(ex);
        }
    }

    /// <summary>
    /// Deletes a row by id.
    /// </summary>
    /// <param name="tableName">Target table name.</param>
    /// <param name="id">Identifier value.</param>
    /// <param name="idColumn">Id column name. Defaults to <see cref="DefaultIdColumn"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure with wrapped error.</returns>
    public async Task<Fin<Unit>> DeleteAsync(
        string tableName,
        object id,
        string? idColumn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(id);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var key = string.IsNullOrWhiteSpace(idColumn) ? DefaultIdColumn : idColumn;
            var sql = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(key)} = @Id";
            await _inner.ExecuteSQLAsync(sql, new Dictionary<string, object?> { ["Id"] = id }, cancellationToken).ConfigureAwait(false);
            return ErrorHandling.SuccessUnit();
        }
        catch (Exception ex)
        {
            return ErrorHandling.Fail<Unit>(ex);
        }
    }

    /// <summary>
    /// Counts rows with an optional where clause.
    /// </summary>
    /// <param name="tableName">Target table name.</param>
    /// <param name="whereClause">Optional SQL where clause body (without WHERE keyword).</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The row count.</returns>
    public Task<long> CountAsync(
        string tableName,
        string? whereClause = null,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        cancellationToken.ThrowIfCancellationRequested();

        var where = string.IsNullOrWhiteSpace(whereClause) ? string.Empty : $" WHERE {whereClause}";
        var sql = $"SELECT COUNT(*) AS TotalCount FROM {QuoteIdentifier(tableName)}{where}";
        var rows = _inner.ExecuteQuery(sql, parameters);

        if (rows.Count == 0)
        {
            return Task.FromResult(0L);
        }

        var first = rows[0];
        if (TryGetValueIgnoreCase(first, "TotalCount", out var value) && value is not null)
        {
            return Task.FromResult(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        var fallback = first.Values.FirstOrDefault();
        return Task.FromResult(fallback is null ? 0L : Convert.ToInt64(fallback, CultureInfo.InvariantCulture));
    }

    private static Option<T> TryMapRow<T>(Dictionary<string, object> row)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            if (typeof(T) == typeof(Dictionary<string, object>))
            {
                return Some((T)(object)row);
            }

            var instance = new T();
            var props = PropertyCache.GetOrAdd(
                typeof(T),
                static type =>
                    type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanWrite)
                        .ToArray());

            foreach (var prop in props)
            {
                if (!TryGetValueIgnoreCase(row, prop.Name, out var value) || value is null)
                {
                    continue;
                }

                var converted = ConvertToPropertyType(value, prop.PropertyType);
                prop.SetValue(instance, converted);
            }

            return Some(instance);
        }
        catch
        {
            return Option<T>.None;
        }
    }

    private static Dictionary<string, object?> BuildEntityParameterMap<T>(T entity)
        where T : class
    {
        var props = PropertyCache.GetOrAdd(
            entity.GetType(),
            static type =>
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead)
                    .ToArray());

        var parameters = new Dictionary<string, object?>(props.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var prop in props)
        {
            parameters[prop.Name] = prop.GetValue(entity);
        }

        return parameters;
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return identifier;
    }

    private static bool TryGetValueIgnoreCase(Dictionary<string, object> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? ConvertToPropertyType(object value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(targetType);

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            if (value is string enumText)
            {
                return Enum.Parse(underlying, enumText, true);
            }

            return Enum.ToObject(underlying, value);
        }

        if (underlying == typeof(Guid))
        {
            return value switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ => new Guid(Convert.ToString(value, CultureInfo.InvariantCulture)!)
            };
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt),
                string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
                _ => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(value, CultureInfo.InvariantCulture))
            };
        }

        if (underlying == typeof(DateTime))
        {
            return value switch
            {
                DateTime dt => dt,
                string s => DateTime.Parse(s, CultureInfo.InvariantCulture),
                _ => DateTime.FromBinary(Convert.ToInt64(value, CultureInfo.InvariantCulture))
            };
        }

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }
}
