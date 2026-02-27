# SharpCoreDB.Provider.Sync

`SharpCoreDB.Provider.Sync` is the Dotmim.Sync provider for SharpCoreDB.

It enables bidirectional synchronization between SharpCoreDB and Dotmim.Sync-supported providers such as SQL Server, PostgreSQL, MySQL, and SQLite.

## Status

- Phase 4 (Testing & Integration): ✅ Complete
- Sync provider test suite: ✅ 84/84 passing
- Target: `.NET 10`, `C# 14`

## Features

### Core Provider Integration

- `SharpCoreDBSyncProvider` implements Dotmim.Sync `CoreProvider`
- Works as client and server provider (`CanBeServerProvider = true`)
- Connection via `SharpCoreDB.Data.Provider` (`SharpCoreDBConnection`)

### Per-Table Sync Adapter

`SharpCoreDBSyncAdapter` supports:

- `SelectChanges`
- `SelectRow`
- `InsertRow`
- `UpdateRow`
- `DeleteRow`
- `SelectMetadata`
- `UpdateMetadata`
- `DeleteMetadata`
- Batch execution (`ExecuteBatchCommandAsync`)

### Change Tracking

- Shadow tracking table per sync table: `{table}_tracking`
- Tracking columns:
  - primary key column (mirrored)
  - `update_scope_id`
  - `timestamp`
  - `sync_row_is_tombstone`
  - `last_change_datetime`
- Timestamp index for delta queries
- Trigger DDL generation for INSERT/UPDATE/DELETE
- Programmatic fallback for engines without trigger support via `RecordChangeAsync`

### Scope Metadata

`SharpCoreDBScopeInfoBuilder` provisions and manages:

- `scope_info`
- `scope_info_client`

Includes full CRUD command generation for both tables plus `SYNC_TIMESTAMP()` support.

### Type Mapping

`SharpCoreDBDbMetadata` provides:

- `DataType -> DbType`
- `DbType -> DataType`
- `DbType -> CLR Type`
- `DataType -> CLR Type`
- `DataType -> SQL type string`
- Sync capability validation (`DataType.Vector` intentionally unsupported)

### Dependency Injection

`SyncServiceCollectionExtensions.AddSharpCoreDBSync()` registers:

- `SyncProviderOptions`
- `SyncProviderFactory`
- `SharpCoreDBSyncProvider`
- `SqliteDialect`
- `TrackingTableBuilder`
- `IChangeTrackingManager` (`ChangeTrackingManager`)
- `ITombstoneManager` (`TombstoneManager`)

### Tombstone Management

`TombstoneManager` supports:

- Cleanup with retention policy (`CleanTombstonesAsync`)
- Count retrieval (`GetTombstoneCountAsync`)

## Configuration

`SyncProviderOptions`:

- `EnableAutoTracking` (default `true`)
- `TombstoneRetentionDays` (default `30`)
- `BatchSize` (default `500`)
- `AutoProvisionScopeTables` (default `true`)
- `CommandTimeoutSeconds` (default `300`)

## Quick Start

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using SharpCoreDB.Provider.Sync;

var local = new SharpCoreDBSyncProvider(
    "Path=C:\\data\\local.scdb;Password=secret",
    new SyncProviderOptions());

var remote = new SqlSyncProvider("Server=sql;Database=app;Trusted_Connection=True;");

var agent = new SyncAgent(local, remote);
var result = await agent.SynchronizeAsync(["Users", "Orders", "Products"]);

Console.WriteLine($"Uploaded: {result.TotalChangesUploaded}");
Console.WriteLine($"Downloaded: {result.TotalChangesDownloaded}");
```

## Integration Test Coverage

Test project: `tests/SharpCoreDB.Provider.Sync.Tests`

### Unit / Component

- `ProviderInitializationTests`
- `DependencyInjectionTests`
- `ChangeTrackingProvisioningTests`
- `PrimaryKeyDebugTests`
- `TypeMappingTests`
- `ScopeInfoBuilderTests`

### Integration

- `SQLiteRoundtripTests`
- `EndToEndSyncScenarios`
- `SyncPerformanceBenchmarks`
- `SyncErrorHandlingTests`

## Running Tests

```bash
# Full provider test suite
dotnet test tests/SharpCoreDB.Provider.Sync.Tests/SharpCoreDB.Provider.Sync.Tests.csproj

# Integration only
dotnet test tests/SharpCoreDB.Provider.Sync.Tests/SharpCoreDB.Provider.Sync.Tests.csproj --filter "Integration"
```

## Final Notes (Phase 4)

Phase 4 delivered complete test infrastructure, scenario coverage, performance benchmarks, and error-path validation.

The sync provider is ready for full-system validation runs and release hardening.
