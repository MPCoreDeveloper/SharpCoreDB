using Dapper;
using SharpCoreDB.Interfaces;
using System.Data;
using System.Diagnostics;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides performance monitoring extensions for Dapper operations.
/// </summary>
public static class DapperPerformanceExtensions
{
    private static readonly Dictionary<string, PerformanceMetrics> _metricsCache = new();
    private static readonly Lock _metricsLock = new();

    /// <summary>
    /// Executes a query with performance tracking.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="sql">The SQL query.</param>
    /// <param name="param">Query parameters.</param>
    /// <param name="queryName">Optional query name for metrics tracking.</param>
    /// <returns>Query results and performance metrics.</returns>
    public static QueryResult<T> QueryWithMetrics<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        string? queryName = null)
    {
        var sw = Stopwatch.StartNew();
        var beforeMemory = GC.GetTotalMemory(false);

        try
        {
            using var connection = database.GetDapperConnection();
            connection.Open();

            var parameters = param != null
                ? ConvertToDynamicParameters(param)
                : null;

            var results = connection.Query<T>(sql, parameters).ToList();
            
            sw.Stop();
            var afterMemory = GC.GetTotalMemory(false);

            var metrics = new PerformanceMetrics
            {
                ExecutionTime = sw.Elapsed,
                MemoryUsed = afterMemory - beforeMemory,
                RowCount = results.Count,
                QueryName = queryName ?? (sql.Length > 50 ? sql[..50] + "..." : sql)
            };

            TrackMetrics(metrics);

            return new QueryResult<T>
            {
                Results = results,
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            throw new QueryPerformanceException(
                $"Query failed after {sw.ElapsedMilliseconds}ms", ex);
        }
    }

    /// <summary>
    /// Executes a query asynchronously with performance tracking.
    /// </summary>
    public static async Task<QueryResult<T>> QueryWithMetricsAsync<T>(
        this IDatabase database,
        string sql,
        object? param = null,
        string? queryName = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var beforeMemory = GC.GetTotalMemory(false);

        try
        {
            using var connection = database.GetDapperConnection();
            connection.Open();

            var parameters = param != null
                ? ConvertToDynamicParameters(param)
                : null;

            var results = (await connection.QueryAsync<T>(sql, parameters)).ToList();
            
            sw.Stop();
            var afterMemory = GC.GetTotalMemory(false);

            var metrics = new PerformanceMetrics
            {
                ExecutionTime = sw.Elapsed,
                MemoryUsed = afterMemory - beforeMemory,
                RowCount = results.Count,
                QueryName = queryName ?? (sql.Length > 50 ? sql[..50] + "..." : sql)
            };

            TrackMetrics(metrics);

            return new QueryResult<T>
            {
                Results = results,
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            throw new QueryPerformanceException(
                $"Query failed after {sw.ElapsedMilliseconds}ms", ex);
        }
    }

    /// <summary>
    /// Executes a command with performance tracking.
    /// </summary>
    public static CommandResult ExecuteWithMetrics(
        this IDatabase database,
        string sql,
        object? param = null,
        string? commandName = null)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var connection = database.GetDapperConnection();
            connection.Open();

            var parameters = param != null
                ? ConvertToDynamicParameters(param)
                : null;

            var affectedRows = connection.Execute(sql, parameters);
            
            sw.Stop();

            var metrics = new PerformanceMetrics
            {
                ExecutionTime = sw.Elapsed,
                RowCount = affectedRows,
                QueryName = commandName ?? (sql.Length > 50 ? sql[..50] + "..." : sql)
            };

            TrackMetrics(metrics);

            return new CommandResult
            {
                AffectedRows = affectedRows,
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            throw new QueryPerformanceException(
                $"Command failed after {sw.ElapsedMilliseconds}ms", ex);
        }
    }

    /// <summary>
    /// Executes a query with a timeout warning.
    /// </summary>
    public static IEnumerable<T> QueryWithTimeout<T>(
        this IDatabase database,
        string sql,
        TimeSpan timeout,
        object? param = null,
        Action<TimeSpan>? onTimeout = null)
    {
        var sw = Stopwatch.StartNew();

        using var connection = database.GetDapperConnection();
        connection.Open();

        var parameters = param != null
            ? ConvertToDynamicParameters(param)
            : null;

        var results = connection.Query<T>(sql, parameters).ToList();
        
        sw.Stop();

        if (sw.Elapsed > timeout)
        {
            onTimeout?.Invoke(sw.Elapsed);
        }

        return results;
    }

    /// <summary>
    /// Gets performance metrics for a specific query.
    /// </summary>
    public static PerformanceMetrics? GetMetrics(string queryName)
    {
        lock (_metricsLock)
        {
            return _metricsCache.TryGetValue(queryName, out var metrics) ? metrics : null;
        }
    }

    /// <summary>
    /// Gets all tracked performance metrics.
    /// </summary>
    public static Dictionary<string, PerformanceMetrics> GetAllMetrics()
    {
        lock (_metricsLock)
        {
            return new Dictionary<string, PerformanceMetrics>(_metricsCache);
        }
    }

    /// <summary>
    /// Clears all performance metrics.
    /// </summary>
    public static void ClearMetrics()
    {
        lock (_metricsLock)
        {
            _metricsCache.Clear();
        }
    }

    /// <summary>
    /// Gets a performance report.
    /// </summary>
    public static PerformanceReport GetPerformanceReport()
    {
        lock (_metricsLock)
        {
            var allMetrics = _metricsCache.Values.ToList();

            if (allMetrics.Count == 0)
            {
                return new PerformanceReport();
            }

            return new PerformanceReport
            {
                TotalQueries = allMetrics.Count,
                AverageExecutionTime = TimeSpan.FromMilliseconds(
                    allMetrics.Average(m => m.ExecutionTime.TotalMilliseconds)),
                TotalExecutionTime = TimeSpan.FromMilliseconds(
                    allMetrics.Sum(m => m.ExecutionTime.TotalMilliseconds)),
                SlowestQuery = allMetrics.OrderByDescending(m => m.ExecutionTime).FirstOrDefault(),
                FastestQuery = allMetrics.OrderBy(m => m.ExecutionTime).FirstOrDefault(),
                TotalMemoryUsed = allMetrics.Sum(m => m.MemoryUsed),
                TotalRowsProcessed = allMetrics.Sum(m => m.RowCount)
            };
        }
    }

    /// <summary>
    /// Executes a batch of queries and tracks overall performance.
    /// </summary>
    public static BatchResult<T> BatchQueryWithMetrics<T>(
        this IDatabase database,
        IEnumerable<string> sqlStatements,
        string? batchName = null)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<List<T>>();
        var queryMetrics = new List<PerformanceMetrics>();

        foreach (var sql in sqlStatements)
        {
            var queryResult = database.QueryWithMetrics<T>(sql);
            results.Add(queryResult.Results);
            queryMetrics.Add(queryResult.Metrics);
        }

        sw.Stop();

        var batchMetrics = new PerformanceMetrics
        {
            ExecutionTime = sw.Elapsed,
            RowCount = queryMetrics.Sum(m => m.RowCount),
            MemoryUsed = queryMetrics.Sum(m => m.MemoryUsed),
            QueryName = batchName ?? $"Batch of {queryMetrics.Count} queries"
        };

        return new BatchResult<T>
        {
            Results = results,
            Metrics = batchMetrics,
            IndividualMetrics = queryMetrics
        };
    }

    private static void TrackMetrics(PerformanceMetrics metrics)
    {
        lock (_metricsLock)
        {
            _metricsCache[metrics.QueryName] = metrics;
        }
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
/// Represents performance metrics for a query.
/// </summary>
public class PerformanceMetrics
{
    public string QueryName { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public long MemoryUsed { get; set; }
    public int RowCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a query result with performance metrics.
/// </summary>
public class QueryResult<T>
{
    public List<T> Results { get; set; } = [];
    public PerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Represents a command result with performance metrics.
/// </summary>
public class CommandResult
{
    public int AffectedRows { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Represents a batch operation result.
/// </summary>
public class BatchResult<T>
{
    public List<List<T>> Results { get; set; } = [];
    public PerformanceMetrics Metrics { get; set; } = new();
    public List<PerformanceMetrics> IndividualMetrics { get; set; } = [];
}

/// <summary>
/// Represents an overall performance report.
/// </summary>
public class PerformanceReport
{
    public int TotalQueries { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public PerformanceMetrics? SlowestQuery { get; set; }
    public PerformanceMetrics? FastestQuery { get; set; }
    public long TotalMemoryUsed { get; set; }
    public long TotalRowsProcessed { get; set; }
}

/// <summary>
/// Exception thrown when a query performance issue is detected.
/// </summary>
public class QueryPerformanceException : Exception
{
    public QueryPerformanceException(string message) : base(message) { }
    public QueryPerformanceException(string message, Exception innerException) 
        : base(message, innerException) { }
}
