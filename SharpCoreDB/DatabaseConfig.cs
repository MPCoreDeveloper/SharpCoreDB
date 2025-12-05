namespace SharpCoreDB;

/// <summary>
/// Configuration options for database performance and behavior.
/// Uses C# 9.0+ init-only properties for immutable configuration.
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// Gets whether encryption should be disabled for maximum performance.
    /// WARNING: Disabling encryption removes AES-256-GCM protection. Use only for trusted environments.
    /// </summary>
    public bool NoEncryptMode { get; init; } = false;

    /// <summary>
    /// Gets whether query caching is enabled.
    /// </summary>
    public bool EnableQueryCache { get; init; } = true;

    /// <summary>
    /// Gets the query cache size limit.
    /// </summary>
    public int QueryCacheSize { get; init; } = 1000;

    /// <summary>
    /// Gets the WAL buffer size in bytes.
    /// </summary>
    public int WalBufferSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Gets whether hash indexes should be used.
    /// </summary>
    public bool EnableHashIndexes { get; init; } = true;

    /// <summary>
    /// Gets whether to use buffered I/O for high-performance mode.
    /// </summary>
    public bool UseBufferedIO { get; init; } = false;

    /// <summary>
    /// Default configuration with encryption enabled.
    /// </summary>
    public static DatabaseConfig Default => new();

    /// <summary>
    /// High-performance configuration with encryption disabled.
    /// </summary>
    public static DatabaseConfig HighPerformance => new()
    {
        NoEncryptMode = true,
        EnableQueryCache = true,
        EnableHashIndexes = true,
        WalBufferSize = 2 * 1024 * 1024, // 2MB
        UseBufferedIO = true
    };
}
