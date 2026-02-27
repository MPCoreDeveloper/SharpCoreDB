# SharpCoreDB.Provider.Sync Changelog

## 1.0.0 (Initial Release)

### Added

- Dotmim.Sync `CoreProvider` implementation via `SharpCoreDBSyncProvider`
- Per-table sync commands via `SharpCoreDBSyncAdapter`
- Provisioning builders:
  - `SharpCoreDBDatabaseBuilder`
  - `SharpCoreDBTableBuilder`
  - `SharpCoreDBScopeInfoBuilder`
- Change tracking services:
  - `IChangeTrackingManager` / `ChangeTrackingManager`
  - `ITombstoneManager` / `TombstoneManager`
  - `TrackingTableBuilder`
- Type mapping service `SharpCoreDBDbMetadata`
- DI integration via `AddSharpCoreDBSync`

### Validation

- Full provider suite passing: **84/84**
- Integration + scenario + performance + error-path tests complete

### Critical stability fixes included in 1.0.0 baseline

- Correct PRIMARY KEY parsing in single-file DDL path
- Complete DI graph registration for change-tracking dependencies
- Programmatic `RecordChangeAsync` for non-trigger engines
- Correct `WHERE` handling in single-file table CRUD path
- Quoted-identifier handling for `DROP TABLE IF EXISTS`
- Support for ORDER BY column position (`ORDER BY 2`)
