// <copyright file="DatabaseConfig.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Configuration options for database performance and behavior.
/// Uses C# 9.0+ init-only properties for immutable configuration.
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// Gets a value indicating whether gets whether encryption should be disabled for maximum performance.
    /// WARNING: Disabling encryption removes AES-256-GCM protection. Use only for trusted environments.
    /// </summary>
    public bool NoEncryptMode { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether gets whether query caching is enabled.
    /// </summary>
    public bool EnableQueryCache { get; init; } = true;

    /// <summary>
    /// Gets the query cache size limit.
    /// </summary>
    public int QueryCacheSize { get; init; } = 1024;

    /// <summary>
    /// Gets the WAL buffer size in bytes.
    /// </summary>
    public int WalBufferSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Gets a value indicating whether gets whether hash indexes should be used.
    /// </summary>
    public bool EnableHashIndexes { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether gets whether to use buffered I/O for high-performance mode.
    /// </summary>
    public bool UseBufferedIO { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether gets whether to use memory-mapped files for improved read performance.
    /// When enabled, files larger than 10 MB will be accessed via memory-mapped I/O,
    /// reducing disk operations and improving SELECT query performance by 30-50%.
    /// </summary>
    public bool UseMemoryMapping { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether gets whether to perform GC.Collect after batch operations for memory cleanup.
    /// </summary>
    public bool CollectGCAfterBatches { get; init; } = false;

    /// <summary>
    /// Gets default configuration with encryption enabled.
    /// </summary>
    public static DatabaseConfig Default => new();

    /// <summary>
    /// Gets high-performance configuration with encryption disabled.
    /// </summary>
    public static DatabaseConfig HighPerformance => new()
    {
        NoEncryptMode = true,
        EnableQueryCache = true,
        EnableHashIndexes = true,
        WalBufferSize = 4 * 1024 * 1024, // 4MB
        UseBufferedIO = true,
        UseMemoryMapping = true,
        CollectGCAfterBatches = true,
    };
}
