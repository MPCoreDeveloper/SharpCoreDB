using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB.Interfaces;
using System.Diagnostics;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Health check for SharpCoreDB database instances with comprehensive diagnostics.
/// </summary>
/// <remarks>
/// Initializes a new instance of the SharpCoreDBHealthCheck class.
/// </remarks>
/// <param name="database">The database instance to check.</param>
/// <param name="options">Health check options.</param>
public class SharpCoreDBHealthCheck(IDatabase database, HealthCheckOptions? options = null) : IHealthCheck
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly HealthCheckOptions _options = options ?? new HealthCheckOptions();

    /// <summary>
    /// Performs the health check.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health check result.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var data = new Dictionary<string, object>();

        try
        {
            // Test 1: Database Connection
            if (_options.TestConnection)
            {
                using var connection = _database.GetDapperConnection();
                connection.Open();
                data["connection"] = "OK";
            }

            // Test 2: Execute Test Query
            if (!string.IsNullOrEmpty(_options.TestQuery))
            {
                var queryStartTime = Stopwatch.StartNew();
                
                if (_options.UseAsync)
                {
                    await _database.ExecuteSQLAsync(_options.TestQuery, cancellationToken);
                }
                else
                {
                    await Task.Run(() => _database.ExecuteSQL(_options.TestQuery), cancellationToken);
                }
                
                queryStartTime.Stop();
                data["query_execution_ms"] = queryStartTime.ElapsedMilliseconds;
            }

            // Test 3: Query Cache Statistics (if enabled)
            if (_options.CheckQueryCache)
            {
                try
                {
                    var cacheStats = _database.GetQueryCacheStatistics();
                    data["cache_hit_rate"] = $"{cacheStats.HitRate:P2}";
                    data["cache_hits"] = cacheStats.Hits;
                    data["cache_misses"] = cacheStats.Misses;
                    data["cached_queries"] = cacheStats.Count;
                }
                catch
                {
                    // Query cache might not be enabled
                    data["cache_status"] = "disabled";
                }
            }

            // Test 4: Table Count (if enabled)
            if (_options.CheckTableCount)
            {
                try
                {
                    using var connection = _database.GetDapperConnection();
                    connection.Open();
                    
                    // This is a simplified check - actual implementation depends on metadata structure
                    var result = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM (SELECT 1) AS HealthCheck");
                    data["test_query_result"] = result;
                }
                catch (Exception ex)
                {
                    data["table_check_warning"] = ex.Message;
                }
            }

            // Test 5: Performance Metrics (if enabled)
            if (_options.CheckPerformanceMetrics)
            {
                var report = DapperPerformanceExtensions.GetPerformanceReport();
                if (report.TotalQueries > 0)
                {
                    data["avg_query_time_ms"] = report.AverageExecutionTime.TotalMilliseconds;
                    data["total_queries"] = report.TotalQueries;
                    
                    if (report.SlowestQuery != null)
                    {
                        data["slowest_query_ms"] = report.SlowestQuery.ExecutionTime.TotalMilliseconds;
                    }
                }
            }

            sw.Stop();
            data["health_check_duration_ms"] = sw.ElapsedMilliseconds;

            // Determine health status based on response time
            if (_options.DegradedThresholdMs.HasValue && sw.ElapsedMilliseconds > _options.DegradedThresholdMs.Value)
            {
                return HealthCheckResult.Degraded(
                    $"SharpCoreDB is responding slowly ({sw.ElapsedMilliseconds}ms)",
                    data: data);
            }

            if (_options.UnhealthyThresholdMs.HasValue && sw.ElapsedMilliseconds > _options.UnhealthyThresholdMs.Value)
            {
                return HealthCheckResult.Unhealthy(
                    $"SharpCoreDB response time exceeded threshold ({sw.ElapsedMilliseconds}ms)",
                    data: data);
            }

            return HealthCheckResult.Healthy("SharpCoreDB is operational", data);
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "SharpCoreDB health check was cancelled",
                data: data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            data["health_check_duration_ms"] = sw.ElapsedMilliseconds;
            data["error"] = ex.Message;
            data["error_type"] = ex.GetType().Name;

            return HealthCheckResult.Unhealthy(
                $"SharpCoreDB health check failed: {ex.Message}",
                ex,
                data);
        }
    }
}

/// <summary>
/// Options for SharpCoreDB health checks.
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// Gets or sets the test query to execute (default: "SELECT 1").
    /// </summary>
    public string TestQuery { get; set; } = "SELECT 1";

    /// <summary>
    /// Gets or sets whether to test database connection (default: true).
    /// </summary>
    public bool TestConnection { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use async execution (default: true).
    /// </summary>
    public bool UseAsync { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check query cache statistics (default: true).
    /// </summary>
    public bool CheckQueryCache { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check table count (default: false).
    /// </summary>
    public bool CheckTableCount { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to check performance metrics (default: true).
    /// </summary>
    public bool CheckPerformanceMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the threshold in milliseconds for degraded status.
    /// </summary>
    public long? DegradedThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the threshold in milliseconds for unhealthy status.
    /// </summary>
    public long? UnhealthyThresholdMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the timeout for health check operations.
    /// </summary>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Extension methods for adding SharpCoreDB health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for SharpCoreDB with basic options.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="testQuery">Optional test query to execute (default: "SELECT 1").</param>
    /// <param name="failureStatus">Status to report on failure (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddSharpCoreDB(
        this IHealthChecksBuilder builder,
        IDatabase database,
        string name = "sharpcoredb",
        string? testQuery = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        var options = new HealthCheckOptions();
        if (!string.IsNullOrEmpty(testQuery))
        {
            options.TestQuery = testQuery;
        }

        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, options),
            failureStatus,
            tags ?? [],
            options.Timeout);
    }

    /// <summary>
    /// Adds a health check for SharpCoreDB with advanced options.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="options">Health check options.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="failureStatus">Status to report on failure (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddSharpCoreDB(
        this IHealthChecksBuilder builder,
        IDatabase database,
        HealthCheckOptions options,
        string name = "sharpcoredb",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, options),
            failureStatus,
            tags ?? [],
            options.Timeout);
    }

    /// <summary>
    /// Adds a health check for SharpCoreDB with custom configuration.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="configure">Action to configure health check options.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="failureStatus">Status to report on failure (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddSharpCoreDB(
        this IHealthChecksBuilder builder,
        IDatabase database,
        Action<HealthCheckOptions> configure,
        string name = "sharpcoredb",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        var options = new HealthCheckOptions();
        configure?.Invoke(options);

        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, options),
            failureStatus,
            tags ?? [],
            options.Timeout);
    }

    /// <summary>
    /// Adds a lightweight health check for SharpCoreDB (connection only).
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="failureStatus">Status to report on failure (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddSharpCoreDBLightweight(
        this IHealthChecksBuilder builder,
        IDatabase database,
        string name = "sharpcoredb",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        var options = new HealthCheckOptions
        {
            TestQuery = string.Empty, // Skip query execution
            CheckQueryCache = false,
            CheckTableCount = false,
            CheckPerformanceMetrics = false,
            TestConnection = true,
            DegradedThresholdMs = 100,
            UnhealthyThresholdMs = 500,
            Timeout = TimeSpan.FromSeconds(2)
        };

        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, options),
            failureStatus,
            tags ?? [],
            options.Timeout);
    }

    /// <summary>
    /// Adds a comprehensive health check for SharpCoreDB (all diagnostics).
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="failureStatus">Status to report on failure (default: Unhealthy).</param>
    /// <param name="tags">Optional tags for the health check.</param>
    /// <returns>The health checks builder.</returns>
    public static IHealthChecksBuilder AddSharpCoreDBComprehensive(
        this IHealthChecksBuilder builder,
        IDatabase database,
        string name = "sharpcoredb",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        var options = new HealthCheckOptions
        {
            TestQuery = "SELECT 1",
            TestConnection = true,
            CheckQueryCache = true,
            CheckTableCount = true,
            CheckPerformanceMetrics = true,
            DegradedThresholdMs = 2000,
            UnhealthyThresholdMs = 10000,
            Timeout = TimeSpan.FromSeconds(30)
        };

        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, options),
            failureStatus,
            tags ?? [],
            options.Timeout);
    }
}
