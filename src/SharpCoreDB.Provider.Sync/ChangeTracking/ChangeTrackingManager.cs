#nullable enable

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Manages change tracking setup and execution.
/// Creates/drops triggers that automatically capture INSERT/UPDATE/DELETE operations
/// in shadow tracking tables for sync change enumeration.
/// </summary>
public sealed class ChangeTrackingManager
{
    // TODO: Implement in Phase 2.2
    // Purpose: Create/drop/verify tracking triggers per table
    // Methods:
    //   - ProvisionTrackingAsync(tableName)
    //   - DeprovisionTrackingAsync(tableName)
    //   - IsProvisionedAsync(tableName)
}
