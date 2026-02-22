#nullable enable

namespace SharpCoreDB.Provider.Sync;

/// <summary>
/// Configuration options for SharpCoreDB Dotmim.Sync provider.
/// Allows customization of change tracking, tombstone cleanup, and sync behavior.
/// </summary>
public sealed class SyncProviderOptions
{
    /// <summary>
    /// Gets or sets whether automatic change tracking provisioning is enabled.
    /// When true, tracking tables and triggers are created automatically during sync setup.
    /// Default: true
    /// </summary>
    public bool EnableAutoTracking { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of days to retain deleted row tombstones.
    /// Older tombstones are purged to prevent unbounded growth.
    /// Default: 30 days
    /// </summary>
    public int TombstoneRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the batch size for bulk insert/update/delete operations.
    /// Larger batches improve throughput but use more memory.
    /// Default: 500 rows
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets whether to automatically create scope metadata tables if they don't exist.
    /// Default: true
    /// </summary>
    public bool AutoProvisionScopeTables { get; set; } = true;

    /// <summary>
    /// Gets or sets the command timeout (in seconds) for sync operations.
    /// Default: 300 seconds (5 minutes)
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 300;
}
