#nullable enable

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Builds DDL for shadow tracking tables.
/// Creates the metadata table structure that records which rows changed and when.
/// </summary>
public sealed class TrackingTableBuilder
{
    // TODO: Implement in Phase 2.1
    // Purpose: Generate DDL for {table}_tracking shadow tables
    // Schema:
    //   CREATE TABLE {table}_tracking (
    //     pk columns,
    //     update_scope_id TEXT,
    //     timestamp BIGINT,
    //     sync_row_is_tombstone INTEGER,
    //     last_change_datetime TEXT,
    //     PRIMARY KEY (pk columns)
    //   )
}
