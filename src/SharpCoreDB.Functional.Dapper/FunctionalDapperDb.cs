namespace SharpCoreDB.Functional.Dapper;

using LanguageExt;
using LanguageExt.Common;
using SharpCoreDB.Extensions;
using SharpCoreDB.Interfaces;
using static LanguageExt.Prelude;

/// <summary>
/// Functional adapter over SharpCoreDB Dapper extension operations with Option/Fin return types.
/// </summary>
/// <param name="database">The underlying SharpCoreDB database instance.</param>
public sealed class FunctionalDapperDb(IDatabase database)
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Gets a single row by id from a table.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <typeparam name="TId">The id type.</typeparam>
    /// <param name="tableName">The table name.</param>
    /// <param name="id">The identifier value.</param>
    /// <param name="idColumn">The id column name.</param>
    /// <returns>Some value when found; none otherwise.</returns>
    public async Task<Option<T>> GetByIdAsync<T, TId>(string tableName, TId id, string idColumn = "Id")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(idColumn);

        var sql = $"SELECT * FROM {tableName} WHERE {idColumn} = @Id LIMIT 1";
        var value = await _database.QueryFirstOrDefaultAsync<T>(sql, new { Id = id }).ConfigureAwait(false);
        return Optional(value);
    }

    /// <summary>
    /// Executes a query expected to return at most one row.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">SQL text.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>Some value when a row exists; none otherwise.</returns>
    public async Task<Option<T>> FindOneAsync<T>(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var value = await _database.QueryFirstOrDefaultAsync<T>(sql, param).ConfigureAwait(false);
        return Optional(value);
    }

    /// <summary>
    /// Executes a query and returns a functional sequence.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="sql">SQL text.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>A sequence of mapped values.</returns>
    public async Task<Seq<T>> QueryAsync<T>(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var rows = await _database.QueryAsync<T>(sql, param).ConfigureAwait(false);
        return toSeq(rows);
    }

    /// <summary>
    /// Executes an INSERT statement and returns a functional result.
    /// </summary>
    /// <param name="sql">SQL text.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>Success when command executes; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> InsertAsync(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        try
        {
            await _database.ExecuteAsync(sql, param).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Executes an UPDATE statement and returns a functional result.
    /// </summary>
    /// <param name="sql">SQL text.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>Success when command executes; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> UpdateAsync(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        try
        {
            await _database.ExecuteAsync(sql, param).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Executes a DELETE statement and returns a functional result.
    /// </summary>
    /// <param name="sql">SQL text.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>Success when command executes; failure with error otherwise.</returns>
    public async Task<Fin<Unit>> DeleteAsync(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        try
        {
            await _database.ExecuteAsync(sql, param).ConfigureAwait(false);
            return FinSucc(unit);
        }
        catch (Exception ex)
        {
            return FinFail<Unit>(Error.New(ex));
        }
    }

    /// <summary>
    /// Executes a COUNT query.
    /// </summary>
    /// <param name="sql">SQL count query.</param>
    /// <param name="param">Optional parameters.</param>
    /// <returns>Count result.</returns>
    public async Task<long> CountAsync(string sql, object? param = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        var value = await _database.ExecuteScalarAsync<long>(sql, param).ConfigureAwait(false);
        return value;
    }
}
