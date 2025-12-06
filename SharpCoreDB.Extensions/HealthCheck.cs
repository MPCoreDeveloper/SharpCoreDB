using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Health check for SharpCoreDB database instances.
/// </summary>
public class SharpCoreDBHealthCheck : IHealthCheck
{
    private readonly IDatabase _database;
    private readonly string _testQuery;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBHealthCheck class.
    /// </summary>
    /// <param name="database">The database instance to check.</param>
    /// <param name="testQuery">Optional test query to execute (default: none).</param>
    public SharpCoreDBHealthCheck(IDatabase database, string? testQuery = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _testQuery = testQuery ?? string.Empty;
    }

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
        try
        {
            // Test database connectivity
            if (!string.IsNullOrEmpty(_testQuery))
            {
                // Execute test query
                await Task.Run(() => _database.ExecuteSQL(_testQuery), cancellationToken);
            }

            return HealthCheckResult.Healthy("SharpCoreDB is operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "SharpCoreDB health check failed",
                ex);
        }
    }
}

/// <summary>
/// Extension methods for adding SharpCoreDB health checks.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds a health check for SharpCoreDB.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="database">The database instance.</param>
    /// <param name="name">The health check name (default: "sharpcoredb").</param>
    /// <param name="testQuery">Optional test query to execute.</param>
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
        return builder.AddCheck(
            name,
            new SharpCoreDBHealthCheck(database, testQuery),
            failureStatus,
            tags ?? []);
    }
}
