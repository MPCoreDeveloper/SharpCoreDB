using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB.Extensions;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Extensions.Examples;

/// <summary>
/// Examples demonstrating SharpCoreDB health check configurations.
/// </summary>
public static class HealthCheckExamples
{
    #region Basic Health Checks

    /// <summary>
    /// Example: Basic health check with default settings.
    /// </summary>
    public static void BasicHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                name: "sharpcoredb",
                testQuery: "SELECT 1");
    }

    /// <summary>
    /// Example: Health check with custom tags.
    /// </summary>
    public static void HealthCheckWithTags(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                name: "sharpcoredb",
                testQuery: "SELECT 1",
                tags: new[] { "database", "sharpcoredb", "ready" });
    }

    /// <summary>
    /// Example: Health check with custom failure status.
    /// </summary>
    public static void HealthCheckWithFailureStatus(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                name: "sharpcoredb",
                testQuery: "SELECT 1",
                failureStatus: HealthStatus.Degraded); // Report as degraded instead of unhealthy
    }

    #endregion

    #region Lightweight Health Checks

    /// <summary>
    /// Example: Lightweight health check (connection only, no query execution).
    /// Best for high-frequency health probes.
    /// </summary>
    public static void LightweightHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDBLightweight(
                database,
                name: "sharpcoredb-lite",
                tags: new[] { "database", "quick" });
    }

    #endregion

    #region Comprehensive Health Checks

    /// <summary>
    /// Example: Comprehensive health check with all diagnostics.
    /// Best for detailed monitoring dashboards.
    /// </summary>
    public static void ComprehensiveHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDBComprehensive(
                database,
                name: "sharpcoredb-detailed",
                tags: new[] { "database", "detailed" });
    }

    #endregion

    #region Custom Configuration

    /// <summary>
    /// Example: Health check with custom options.
    /// </summary>
    public static void CustomOptionsHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                options: new HealthCheckOptions
                {
                    TestQuery = "SELECT COUNT(*) FROM Users",
                    TestConnection = true,
                    CheckQueryCache = true,
                    CheckPerformanceMetrics = true,
                    DegradedThresholdMs = 500,    // Degraded if > 500ms
                    UnhealthyThresholdMs = 2000,  // Unhealthy if > 2000ms
                    Timeout = TimeSpan.FromSeconds(5)
                },
                name: "sharpcoredb-custom");
    }

    /// <summary>
    /// Example: Health check with lambda configuration.
    /// </summary>
    public static void LambdaConfigurationHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = "SELECT 1";
                    options.CheckQueryCache = true;
                    options.CheckPerformanceMetrics = true;
                    options.DegradedThresholdMs = 1000;
                    options.UnhealthyThresholdMs = 5000;
                },
                name: "sharpcoredb");
    }

    #endregion

    #region Multiple Health Checks

    /// <summary>
    /// Example: Multiple health checks with different configurations.
    /// </summary>
    public static void MultipleHealthChecks(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            // Quick liveness check
            .AddSharpCoreDBLightweight(
                database,
                name: "sharpcoredb-liveness",
                tags: new[] { "liveness", "quick" })
            
            // Detailed readiness check
            .AddSharpCoreDBComprehensive(
                database,
                name: "sharpcoredb-readiness",
                tags: new[] { "readiness", "detailed" });
    }

    #endregion

    #region Performance-Focused Health Checks

    /// <summary>
    /// Example: Performance-focused health check.
    /// </summary>
    public static void PerformanceFocusedHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = "SELECT 1";
                    options.CheckQueryCache = true;
                    options.CheckPerformanceMetrics = true;
                    options.CheckTableCount = false;
                    
                    // Strict performance thresholds
                    options.DegradedThresholdMs = 100;   // < 100ms = healthy
                    options.UnhealthyThresholdMs = 500;  // > 500ms = unhealthy
                    options.Timeout = TimeSpan.FromSeconds(1);
                },
                name: "sharpcoredb-perf",
                tags: new[] { "performance", "database" });
    }

    #endregion

    #region Cache Monitoring Health Checks

    /// <summary>
    /// Example: Cache-focused health check.
    /// </summary>
    public static void CacheMonitoringHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = string.Empty; // Skip query
                    options.TestConnection = false;
                    options.CheckQueryCache = true;
                    options.CheckPerformanceMetrics = true;
                    options.CheckTableCount = false;
                },
                name: "sharpcoredb-cache",
                tags: new[] { "cache", "metrics" });
    }

    #endregion

    #region Kubernetes/Container Examples

    /// <summary>
    /// Example: Kubernetes-style liveness probe.
    /// Fast check to determine if the database is alive.
    /// </summary>
    public static void KubernetesLivenessProbe(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = string.Empty;
                    options.TestConnection = true;
                    options.CheckQueryCache = false;
                    options.CheckPerformanceMetrics = false;
                    options.CheckTableCount = false;
                    options.DegradedThresholdMs = null; // No degraded status
                    options.UnhealthyThresholdMs = 1000;
                    options.Timeout = TimeSpan.FromSeconds(2);
                },
                name: "liveness",
                tags: new[] { "k8s", "liveness" });
    }

    /// <summary>
    /// Example: Kubernetes-style readiness probe.
    /// More thorough check to determine if the database is ready to serve traffic.
    /// </summary>
    public static void KubernetesReadinessProbe(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = "SELECT 1";
                    options.TestConnection = true;
                    options.CheckQueryCache = true;
                    options.CheckPerformanceMetrics = true;
                    options.DegradedThresholdMs = 500;
                    options.UnhealthyThresholdMs = 2000;
                    options.Timeout = TimeSpan.FromSeconds(5);
                },
                name: "readiness",
                tags: new[] { "k8s", "readiness" });
    }

    #endregion

    #region Production-Ready Configuration

    /// <summary>
    /// Example: Production-ready health check configuration.
    /// </summary>
    public static void ProductionHealthCheck(IServiceCollection services, IDatabase database)
    {
        services.AddHealthChecks()
            // Fast liveness check (every 5 seconds)
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = string.Empty;
                    options.TestConnection = true;
                    options.CheckQueryCache = false;
                    options.CheckPerformanceMetrics = false;
                    options.UnhealthyThresholdMs = 1000;
                    options.Timeout = TimeSpan.FromSeconds(2);
                },
                name: "sharpcoredb-live",
                tags: new[] { "liveness" })
            
            // Detailed readiness check (every 30 seconds)
            .AddSharpCoreDB(
                database,
                configure: options =>
                {
                    options.TestQuery = "SELECT 1";
                    options.TestConnection = true;
                    options.CheckQueryCache = true;
                    options.CheckPerformanceMetrics = true;
                    options.DegradedThresholdMs = 1000;
                    options.UnhealthyThresholdMs = 5000;
                    options.Timeout = TimeSpan.FromSeconds(10);
                },
                name: "sharpcoredb-ready",
                tags: new[] { "readiness" })
            
            // Deep diagnostic check (manual/on-demand)
            .AddSharpCoreDBComprehensive(
                database,
                name: "sharpcoredb-diagnostic",
                tags: new[] { "diagnostic", "detailed" });
    }

    #endregion

    #region Using Health Check Results

    /// <summary>
    /// Example: Consuming health check results.
    /// </summary>
    public static async Task ConsumeHealthCheckResults(HealthCheckService healthCheckService)
    {
        // Check overall health
        var report = await healthCheckService.CheckHealthAsync();
        
        Console.WriteLine($"Overall Status: {report.Status}");
        Console.WriteLine($"Duration: {report.TotalDuration}");

        // Check individual health checks
        foreach (var entry in report.Entries)
        {
            Console.WriteLine($"\nHealth Check: {entry.Key}");
            Console.WriteLine($"  Status: {entry.Value.Status}");
            Console.WriteLine($"  Duration: {entry.Value.Duration}");
            Console.WriteLine($"  Description: {entry.Value.Description}");

            if (entry.Value.Data.Count > 0)
            {
                Console.WriteLine("  Data:");
                foreach (var data in entry.Value.Data)
                {
                    Console.WriteLine($"    {data.Key}: {data.Value}");
                }
            }

            if (entry.Value.Exception != null)
            {
                Console.WriteLine($"  Exception: {entry.Value.Exception.Message}");
            }
        }
    }

    #endregion

    #region ASP.NET Core Integration

    /// <summary>
    /// Example: Complete ASP.NET Core setup with health checks.
    /// Note: This example shows the pattern - actual implementation requires
    /// Microsoft.AspNetCore.Diagnostics.HealthChecks package.
    /// </summary>
    public static class AspNetCoreSetup
    {
        public static void ConfigureServices(IServiceCollection services, IDatabase database)
        {
            // Add health checks
            services.AddHealthChecks()
                .AddSharpCoreDB(
                    database,
                    name: "sharpcoredb",
                    testQuery: "SELECT 1",
                    tags: new[] { "database", "ready" });

            // Add health check UI (optional)
            // Requires: Microsoft.Extensions.Diagnostics.HealthChecks.UI
            // services.AddHealthChecksUI();
        }

        // ASP.NET Core endpoint configuration example (requires ASP.NET Core packages):
        // app.UseHealthChecks("/health");
        // 
        // app.UseHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        // {
        //     Predicate = check => check.Tags.Contains("ready"),
        //     ResponseWriter = async (context, report) =>
        //     {
        //         context.Response.ContentType = "application/json";
        //         var result = System.Text.Json.JsonSerializer.Serialize(new
        //         {
        //             status = report.Status.ToString(),
        //             duration = report.TotalDuration,
        //             checks = report.Entries.Select(e => new
        //             {
        //                 name = e.Key,
        //                 status = e.Value.Status.ToString(),
        //                 duration = e.Value.Duration,
        //                 description = e.Value.Description,
        //                 data = e.Value.Data
        //             })
        //         });
        //         await context.Response.WriteAsync(result);
        //     }
        // });
        //
        // app.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        // {
        //     Predicate = check => check.Tags.Contains("liveness")
        // });
    }

    #endregion
}
