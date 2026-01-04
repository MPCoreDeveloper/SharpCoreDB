using Dapper;
using SharpCoreDB.Interfaces;
using System.Data;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides async extension methods for Dapper with SharpCoreDB.
/// </summary>
public static class DapperAsyncExtensions
{
    /// <summary>
    /// Executes a query asynchronously and returns the results as dynamic objects.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query results as dynamic objects.</returns>
    public static async Task<IEnumerable<dynamic>> QueryAsync(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QueryAsync(sql, parameters);
    }

    /// <summary>
    /// Executes a query asynchronously and returns the results as strongly-typed objects.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query results.</returns>
    public static async Task<IEnumerable<T>> QueryAsync<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QueryAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Executes a query asynchronously and returns a single result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first result or default.</returns>
    public static async Task<T?> QueryFirstOrDefaultAsync<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Executes a query asynchronously and returns a single result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The single result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no results or multiple results are found.</exception>
    public static async Task<T> QuerySingleAsync<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Executes a command asynchronously and returns the number of affected rows.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL command.</param>
    /// <param name="param">The command parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    public static async Task<int> ExecuteAsync(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>
    /// Executes a scalar query asynchronously.
    /// </summary>
    /// <typeparam name="T">The scalar result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scalar result.</returns>
    public static async Task<T?> ExecuteScalarAsync<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Executes multiple queries asynchronously.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL containing multiple queries.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A multi-result reader.</returns>
    public static async Task<SqlMapper.GridReader> QueryMultipleAsync(
        this IDatabase database,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QueryMultipleAsync(sql, parameters);
    }

    /// <summary>
    /// Executes a query with pagination asynchronously.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="param">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result.</returns>
    public static async Task<PagedResult<T>> QueryPagedAsync<T>(
        this IDatabase database,
        string sql,
        int pageNumber,
        int pageSize,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (pageNumber < 1)
            throw new ArgumentException("Page number must be at least 1", nameof(pageNumber));
        
        if (pageSize < 1)
            throw new ArgumentException("Page size must be at least 1", nameof(pageSize));

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM ({sql}) AS CountQuery";
        var totalCount = await connection.ExecuteScalarAsync<long>(countSql, parameters);

        // Get paged data
        var offset = (pageNumber - 1) * pageSize;
        var pagedSql = $"{sql} LIMIT {pageSize} OFFSET {offset}";
        var items = await connection.QueryAsync<T>(pagedSql, parameters);

        return new PagedResult<T>
        {
            Items = items.ToList(),
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Executes a stored procedure asynchronously (if supported in future).
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="procedureName">The stored procedure name.</param>
    /// <param name="param">The procedure parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The procedure results.</returns>
    public static async Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(
        this IDatabase database,
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureName);

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        return await connection.QueryAsync<T>(
            procedureName, 
            parameters, 
            commandType: CommandType.StoredProcedure);
    }

    private static DynamicParameters ConvertToDynamicParameters(object param)
    {
        if (param is DynamicParameters dp)
            return dp;

        var dynamicParams = new DynamicParameters();
        
        var properties = param.GetType().GetProperties();
        foreach (var prop in properties)
        {
            var value = prop.GetValue(param);
            dynamicParams.Add(prop.Name, value);
        }

        return dynamicParams;
    }
}

/// <summary>
/// Represents a paginated query result.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Gets or sets the items in the current page.
    /// </summary>
    public List<T> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the current page number (1-based).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the total count of items.
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Gets whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Gets whether there is a next page.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;
}
