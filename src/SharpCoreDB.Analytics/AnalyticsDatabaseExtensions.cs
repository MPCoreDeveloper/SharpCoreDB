namespace SharpCoreDB.Analytics;

using SharpCoreDB.Analytics.OLAP;
using SharpCoreDB.Interfaces;

/// <summary>
/// Provides SharpCoreDB analytics extensions for database queries.
/// </summary>
public static class AnalyticsDatabaseExtensions
{
    /// <summary>
    /// Executes a query and maps each row to an analytics record.
    /// </summary>
    /// <typeparam name="T">The analytics record type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="map">The row mapping function.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>A read-only list of mapped records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the database or mapping function is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the SQL query is null or whitespace.</exception>
    public static IReadOnlyList<T> QueryAnalytics<T>(
        this IDatabase database,
        string sql,
        Func<Dictionary<string, object>, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var rows = database.ExecuteQuery(sql, parameters);
        if (rows.Count == 0)
        {
            return [];
        }

        List<T> results = new(rows.Count);
        foreach (var row in rows)
        {
            results.Add(map(row));
        }

        return results;
    }

    /// <summary>
    /// Executes a query and maps the results into an OLAP cube.
    /// </summary>
    /// <typeparam name="T">The analytics record type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="map">The row mapping function.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>An OLAP cube built from the query results.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the database or mapping function is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the SQL query is null or whitespace.</exception>
    public static OlapCube<T> QueryOlapCube<T>(
        this IDatabase database,
        string sql,
        Func<Dictionary<string, object>, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new OlapCube<T>(database.QueryAnalytics(sql, map, parameters));
    }
}
