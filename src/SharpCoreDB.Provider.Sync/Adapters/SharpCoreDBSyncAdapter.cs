#nullable enable

namespace SharpCoreDB.Provider.Sync.Adapters;

/// <summary>
/// Per-table sync adapter that handles change enumeration and application.
/// Queries changed rows, applies remote changes, and detects conflicts.
/// </summary>
public sealed class SharpCoreDBSyncAdapter
{
    // TODO: Implement in Phase 3
    // Purpose: Per-table DML (select changes, apply changes)
    // Methods:
    //   - SelectChangesAsync()
    //   - ApplyChangesAsync()
    //   - Bulk insert/update/delete
    //   - Conflict detection & resolution
}
