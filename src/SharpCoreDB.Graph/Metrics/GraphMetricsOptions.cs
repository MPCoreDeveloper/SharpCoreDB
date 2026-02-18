namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Configuration options for graph metrics collection.
/// C# 14: Primary constructor with init-only properties.
/// </summary>
public sealed class GraphMetricsOptions
{
    /// <summary>
    /// Enable metrics collection. Default: false (zero overhead).
    /// </summary>
    public bool EnableMetrics { get; init; }
    
    /// <summary>
    /// Enable OpenTelemetry tracing. Default: false.
    /// </summary>
    public bool EnableTracing { get; init; }
    
    /// <summary>
    /// Sanitize node IDs before export (hash for privacy). Default: true.
    /// </summary>
    public bool SanitizeNodeIds { get; init; } = true;
    
    /// <summary>
    /// Maximum baggage size in bytes. Default: 1024.
    /// </summary>
    public int MaxBaggageSize { get; init; } = 1024;
    
    /// <summary>
    /// Allowed baggage keys (whitelist). Empty = allow all.
    /// </summary>
    public string[] AllowedBaggageKeys { get; init; } = [];
    
    /// <summary>
    /// Custom metrics collector instance. Default: null (use global).
    /// </summary>
    public GraphMetricsCollector? MetricsCollector { get; init; }
    
    /// <summary>Default options with metrics disabled.</summary>
    public static GraphMetricsOptions Default => new() { EnableMetrics = false };
    
    /// <summary>Options with metrics enabled for development.</summary>
    public static GraphMetricsOptions Development => new() 
    { 
        EnableMetrics = true,
        EnableTracing = true,
        SanitizeNodeIds = false // Allow raw IDs in dev
    };
    
    /// <summary>Options with metrics enabled for production.</summary>
    public static GraphMetricsOptions Production => new()
    {
        EnableMetrics = true,
        EnableTracing = true,
        SanitizeNodeIds = true, // Always hash in production
        AllowedBaggageKeys = ["graph.queryId", "graph.userId"]
    };
}
