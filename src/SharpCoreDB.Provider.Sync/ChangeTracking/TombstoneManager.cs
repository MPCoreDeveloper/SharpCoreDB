#nullable enable

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Manages cleanup of deleted row tombstones.
/// Removes old tombstone records after retention period to prevent unbounded growth.
/// </summary>
public sealed class TombstoneManager
{
    // TODO: Implement in Phase 2.6
    // Purpose: Manage deleted row tracking and periodic cleanup
    // Methods:
    //   - CleanTombstonesAsync(retentionDays)
    //   - GetTombstoneCountAsync(tableName)
    // Config: Default retention: 30 days (configurable)
}
