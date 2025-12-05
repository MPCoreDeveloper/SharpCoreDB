namespace SharpCoreDB;

/// <summary>
/// Configuration options for database performance and behavior.
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// Gets or sets whether encryption should be disabled for maximum performance.
    /// WARNING: Disabling encryption removes AES-256-GCM protection. Use only for trusted environments.
    /// </summary>
    public bool NoEncryptMode { get; set; } = false;

    /// <summary>
    /// Gets or sets whether query caching is enabled.
    /// </summary>
    public bool EnableQueryCache { get; set; } = true;

    /// <summary>
    /// Gets or sets the query cache size limit.
    /// </summary>
    public int QueryCacheSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the WAL buffer size in bytes.
    /// </summary>
    public int WalBufferSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Gets or sets whether hash indexes should be used.
    /// </summary>
    public bool EnableHashIndexes { get; set; } = true;

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
        WalBufferSize = 2 * 1024 * 1024 // 2MB
    };
}
