# Dotmim.Sync Provider for SharpCoreDB — Implementation Plan

**Parent Proposal:** [DOTMIM_SYNC_PROVIDER_PROPOSAL.md](./DOTMIM_SYNC_PROVIDER_PROPOSAL.md)  
**Estimated Total Effort:** 5-7 weeks  
**Target Version:** SharpCoreDB.Provider.Sync v1.0.0

---

## Phase Overview

| Phase | Name | Duration | Deliverable |
|---|---|---|---|
| **Phase 0** | Prerequisites & Infrastructure | 1 week | Change tracking primitives in core engine |
| **Phase 1** | Core Provider Skeleton | 1 week | Compilable add-in with Dotmim.Sync wiring |
| **Phase 2** | Change Tracking & Metadata | 2 weeks | Shadow tables, triggers, scope management |
| **Phase 3** | Sync Adapter (DML) | 1-2 weeks | Select changes, apply changes, conflict resolution |
| **Phase 4** | Testing & Integration | 1-2 weeks | Unit tests, integration tests, SQLite roundtrip |
| **Phase 5** | Filter Support | 0.5 weeks | Multi-tenant filtered sync |
| **Phase 6** | Polish & Documentation | 0.5 weeks | NuGet packaging, README, samples |

---

## Phase 0: Prerequisites & Infrastructure (Week 1)

### Goal
Add minimal primitives to the SharpCoreDB core engine that the sync provider needs.

### Tasks

#### 0.1 Add GUID DataType Support
- **File:** `src/SharpCoreDB/DataTypes.cs`
- **Change:** Add `GUID` to the `DataType` enum (or verify TEXT storage is sufficient for sync scope IDs)
- **Validation:** Existing `ULID` type proves the pattern; GUID may be stored as TEXT internally

#### 0.2 Extend Trigger System for Sync Tracking
- **File:** `src/SharpCoreDB/Services/SqlParser.Triggers.cs`
- **Change:** Verify trigger body can execute INSERT and UPDATE statements against tracking tables
- **Test:** Create trigger that INSERTs into a second table on every INSERT to the first table
- **Risk:** Current `FireTriggers()` uses `ExecuteInternal()` — confirm this works for cross-table DML

#### 0.3 Add Schema Introspection API
- **File:** `src/SharpCoreDB/Interfaces/IDatabase.cs` (or new interface)
- **Change:** Add `GetTableNames()`, `GetTableSchema(tableName)` methods if not already exposed
- **Note:** `ITable` already exposes `Columns`, `ColumnTypes`, `PrimaryKeyIndex` — may just need `IDatabase.GetTable(name)`

#### 0.4 Verify JOIN Performance with Tracking Tables
- **File:** Tests
- **Change:** Benchmark `SELECT t.*, tt.* FROM table t JOIN table_tracking tt ON t.pk = tt.pk WHERE tt.timestamp > @ts`
- **Goal:** Ensure hash index on tracking PK keeps JOIN performant for 10K+ row tables

#### 0.5 Add Tick-Based Timestamp Function
- **File:** `src/SharpCoreDB/Services/SqlFunctions.cs`
- **Change:** Add `SYNC_TIMESTAMP()` SQL function returning `DateTimeOffset.UtcNow.Ticks` (monotonic long)
- **Rationale:** Dotmim.Sync uses long tick timestamps for ordering changes

### Deliverable
- All prerequisites pass unit tests
- No breaking changes to existing APIs

---

## Phase 1: Core Provider Skeleton (Week 2)

### Goal
Create the `SharpCoreDB.Provider.Sync` add-in project with a compilable `CoreProvider` implementation that Dotmim.Sync can instantiate.

### Tasks

#### 1.1 Create Project Structure (Add-In Pattern)
```
src/SharpCoreDB.Provider.Sync/              ← Named like SharpCoreDB.Provider.YesSql add-in
  SharpCoreDB.Provider.Sync.csproj          ← net10.0, refs Dotmim.Sync.Core + SharpCoreDB.Data.Provider
  SharpCoreDBSyncProvider.cs                ← CoreProvider implementation
  Builders/
    SharpCoreDBDatabaseBuilder.cs
    SharpCoreDBTableBuilder.cs
    SharpCoreDBScopeInfoBuilder.cs
  Adapters/
    SharpCoreDBSyncAdapter.cs
    SharpCoreDBObjectNames.cs
  Metadata/
    SharpCoreDBDbMetadata.cs
    SharpCoreDBSchemaReader.cs
  ChangeTracking/
    ChangeTrackingManager.cs
    TrackingTableBuilder.cs
    TombstoneManager.cs
  Extensions/
    SyncServiceCollectionExtensions.cs    ← DI registration (AddSharpCoreDBSync)
    SyncProviderFactory.cs                ← Factory pattern for provider instantiation

tests/SharpCoreDB.Provider.Sync.Tests/
  SharpCoreDB.Provider.Sync.Tests.csproj
  [unit and integration test files...]  
```

#### 1.2 Implement SharpCoreDBSyncProvider
- Inherit from `CoreProvider`
- Override `CreateConnection()` → return `SharpCoreDBConnection`
- Override builder factory methods (return stubs initially)
- Implement `GetDatabaseName()` from connection string

#### 1.3 Implement SharpCoreDBDbMetadata
- Type mapping: SharpCoreDB `DataType` ↔ `DbType` ↔ .NET types
- Column metadata: max length, precision, scale
- Validation: unsupported types throw meaningful errors

#### 1.4 Add DI Extensions & Factory Pattern
- **File:** `src/SharpCoreDB.Provider.Sync/Extensions/SyncServiceCollectionExtensions.cs`
- **Pattern:** Follow `SharpCoreDB.Provider.YesSql` DI pattern
- **Method:** `IServiceCollection.AddSharpCoreDBSync(connectionString, options)`
- **Registrations:**
  - `SharpCoreDBSyncProvider` → singleton (factory creates instances)
  - `SyncProviderFactory` → singleton (creates configured providers)
  - `IChangeTrackingManager` → singleton
  - `ITombstoneManager` → singleton
  - `SyncProviderOptions` → singleton (configuration)
- **Usage example:**
  ```csharp
  // Program.cs
  services.AddSharpCoreDBSync(
      connectionString: "Path=C:\\data\\local.scdb;Password=secret",
      options: opts => {
          opts.EnableAutoTracking = true;
          opts.TombstoneRetentionDays = 30;
      });
  
  // Later: resolve from container
  var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
  ```

### Deliverable
- `SharpCoreDBSyncProvider` compiles and can be instantiated
- DI extensions properly registered
- Dotmim.Sync `SyncAgent` accepts the provider without runtime errors (operations throw `NotImplementedException`)

---

## Phase 2: Change Tracking & Metadata (Weeks 3-4)

### Goal
Implement the full change tracking system (shadow tables, triggers) and scope metadata management.

### Tasks

#### 2.1 Implement TrackingTableBuilder
- **Purpose:** Generate DDL for `{table}_tracking` shadow tables
- **Input:** `ITable` schema (columns, PK, types)
- **Output:** `CREATE TABLE {table}_tracking (pk columns, update_scope_id TEXT, timestamp BIGINT, sync_row_is_tombstone INTEGER, last_change_datetime TEXT)`
- **Index:** Hash index on PK columns; B-tree index on `timestamp`

#### 2.2 Implement ChangeTrackingManager
- **Purpose:** Create/drop/verify tracking triggers per table
- **Operations:**
  - `ProvisionTrackingAsync(tableName)` — Creates tracking table + 3 triggers (insert/update/delete)
  - `DeprovisionTrackingAsync(tableName)` — Drops tracking table + triggers
  - `IsProvisionedAsync(tableName)` — Checks if tracking is set up
- **Trigger SQL:** Uses SharpCoreDB's `CREATE TRIGGER ... AFTER INSERT/UPDATE/DELETE ON {table} BEGIN ... END`
- **Critical:** Trigger bodies must use `NEW.*` / `OLD.*` references correctly

#### 2.3 Implement SharpCoreDBScopeInfoBuilder
- **Purpose:** CRUD operations on `scope_info` and `scope_info_client` tables
- **Methods:**
  - `EnsureScopeInfoTableAsync()` — Creates scope tables if not exist
  - `GetScopeInfoAsync(scopeName)` — Read scope metadata
  - `SaveScopeInfoAsync(scopeInfo)` — Write/update scope metadata
  - `GetScopeInfoClientAsync(scopeId, scopeName)` — Read client scope
  - `SaveScopeInfoClientAsync(scopeInfoClient)` — Write/update client scope
- **Schema:** See proposal Section 4.2

#### 2.4 Implement SharpCoreDBTableBuilder
- **Purpose:** DDL operations for provisioning user tables
- **Methods:**
  - `CreateTableAsync(table)` — Generate CREATE TABLE from SyncTable schema
  - `CreateTrackingTableAsync(table)` — Delegate to TrackingTableBuilder
  - `CreateTriggersAsync(table)` — Delegate to ChangeTrackingManager
  - `CreatePrimaryKeyAsync(table)` — SharpCoreDB PKs are set at CREATE TABLE time
  - `DropTableAsync(table)` — DROP TABLE + cleanup tracking
- **Type Mapping:** Use SharpCoreDBDbMetadata for column type generation

#### 2.5 Implement SharpCoreDBDatabaseBuilder
- **Purpose:** Database-level provisioning
- **Methods:**
  - `EnsureDatabaseAsync()` — Verify SharpCoreDB database exists and is accessible
  - `GetDatabaseVersionAsync()` — Return SharpCoreDB version
- **Note:** SharpCoreDB databases are auto-created on `Initialize()`; this is mostly a connectivity check

#### 2.6 Implement TombstoneManager
- **Purpose:** Manage deleted row tracking and periodic cleanup
- **Methods:**
  - `CleanTombstonesAsync(retentionDays)` — Purge old tombstones
  - `GetTombstoneCountAsync(tableName)` — Diagnostics
- **Config:** Default retention: 30 days (configurable)

### Deliverable
- Tracking tables and triggers are correctly provisioned
- Scope metadata tables are created and populated
- Unit tests verify trigger-based change capture for INSERT/UPDATE/DELETE

---

## Phase 3: Sync Adapter — DML Operations (Weeks 5-6)

### Goal
Implement the per-table sync adapter that handles change enumeration and application.

### Tasks

#### 3.1 Implement SharpCoreDBObjectNames
- **Purpose:** SQL command text templates for all sync operations
- **Templates:**
  - `SelectChangesCommand` — SELECT with JOIN on tracking table, filtered by timestamp
  - `SelectRowCommand` — SELECT single row by PK (for conflict detection)
  - `InsertCommand` — INSERT with conflict check
  - `UpdateCommand` — UPDATE by PK with timestamp validation
  - `DeleteCommand` — DELETE by PK
  - `InsertTrackingCommand` — INSERT into tracking table
  - `UpdateTrackingCommand` — UPDATE tracking table
  - `BulkInsertCommand` — Batched INSERT using ExecuteBatchSQL
  - `BulkUpdateCommand` — Batched UPDATE
  - `BulkDeleteCommand` — Batched DELETE
  - `ResetCommand` — Clear tracking data for re-sync
- **Parameterization:** Use `@param` syntax compatible with SharpCoreDB's parameterized queries

#### 3.2 Implement SharpCoreDBSyncAdapter — Select Changes
- **Input:** Last sync timestamp, scope ID, optional filter parameters
- **Output:** `IEnumerable<SyncRow>` of changed rows with tracking metadata
- **Query:** See proposal Section 4.1.3
- **Performance:** Use hash index on tracking PK for JOIN; B-tree on timestamp for range filter

#### 3.3 Implement SharpCoreDBSyncAdapter — Apply Changes
- **Insert:** Map `SyncRow` → `Dictionary<string, object>` → `ITable.Insert()` or `InsertBatch()`
- **Update:** Map `SyncRow` → `ExecuteSQL("UPDATE ... SET ... WHERE pk = @pk")`
- **Delete:** Map `SyncRow` → `ExecuteSQL("DELETE FROM ... WHERE pk = @pk")`
- **Tracking:** Update `{table}_tracking` with new timestamp and scope_id after each operation
- **Batch Mode:** Use `IDatabase.BeginBatchUpdate()` / `EndBatchUpdate()` for atomic apply

#### 3.4 Implement Conflict Detection
- **Logic:**
  1. Before applying a remote change, check if local row was also modified (tracking timestamp > last sync)
  2. If conflict detected, delegate to Dotmim.Sync's conflict resolution policy
  3. Apply winner's version; update tracking with resolution result
- **Policies:** Server-wins (default), client-wins, custom `ConflictResolutionAction`

#### 3.5 Implement Bulk Operations
- **BulkInsert:** Accumulate rows → `ITable.InsertBatch(rows)` → single WAL flush
- **BulkUpdate:** Accumulate UPDATE SQL → `IDatabase.ExecuteBatchSQL(statements)`
- **BulkDelete:** Accumulate DELETE SQL → `IDatabase.ExecuteBatchSQL(statements)`
- **Batch Size:** Default 500 rows per batch (configurable)
- **Transaction:** Wrap each batch in `BeginBatchUpdate()` / `EndBatchUpdate()`

#### 3.6 Implement SharpCoreDBSchemaReader
- **Purpose:** Read existing table schemas for sync setup auto-discovery
- **Methods:**
  - `GetTablesAsync()` — List all user tables (exclude tracking/scope tables)
  - `GetColumnsAsync(tableName)` — Read column metadata from `ITable`
  - `GetPrimaryKeysAsync(tableName)` — Read PK from `ITable.PrimaryKeyIndex`
  - `GetRelationsAsync(tableName)` — Read FKs from `ITable.ForeignKeys`

### Deliverable
- Full sync cycle works: provision → select changes → apply changes → update scope
- Conflict detection and resolution functions correctly
- Bulk operations use optimized batch paths

---

## Phase 4: Testing & Integration (Weeks 7-8)

### Goal
Comprehensive test coverage and end-to-end integration testing.

### Tasks

#### 4.1 Unit Tests — Change Tracking
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/ChangeTrackingTests.cs
[Fact] WhenRowInserted_TrackingTableContainsEntry()
[Fact] WhenRowUpdated_TrackingTimestampUpdated()
[Fact] WhenRowDeleted_TombstoneCreated()
[Fact] WhenSelectChangesSinceTimestamp_ReturnsOnlyNewChanges()
[Fact] WhenTombstoneExpired_CleanupRemovesIt()
```

#### 4.2 Unit Tests — Type Mapping
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/TypeMappingTests.cs
[Theory]
[InlineData(DataType.INTEGER, DbType.Int64)]
[InlineData(DataType.TEXT, DbType.String)]
[InlineData(DataType.REAL, DbType.Double)]
WhenMappingDataType_ReturnsCorrectDbType(DataType input, DbType expected)
```

#### 4.3 Unit Tests — Scope Management
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/ScopeInfoTests.cs
[Fact] WhenScopeCreated_CanBeRetrieved()
[Fact] WhenScopeUpdated_TimestampChanges()
[Fact] WhenMultipleScopes_EachHasIndependentState()
```

#### 4.4 Unit Tests — Conflict Resolution
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/ConflictResolutionTests.cs
[Fact] WhenBothSidesModified_ServerWinsPolicy_ServerVersionApplied()
[Fact] WhenBothSidesModified_ClientWinsPolicy_ClientVersionApplied()
[Fact] WhenInsertConflict_DuplicatePK_ConflictRaised()
```

#### 4.5 Integration Tests — SharpCoreDB ↔ SQLite Roundtrip
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/Integration/BasicSyncIntegrationTests.cs
[Fact] WhenServerHasNewRows_ClientReceivesThemAfterSync()
[Fact] WhenClientHasNewRows_ServerReceivesThemAfterSync()
[Fact] WhenBidirectionalChanges_BothSidesConverge()
[Fact] WhenLargeDataset_10KRows_SyncsInUnder5Seconds()
```

#### 4.6 Integration Tests — Encrypted Database (Sanity Check)
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/Integration/EncryptedSyncTests.cs
// NOTE: Encryption is at-rest only and fully transparent to the sync provider.
// These tests just verify sync works the same on encrypted databases (it should — no special code paths).
[Fact] WhenDatabaseEncrypted_SyncWorksIdenticallyToUnencrypted()
```

#### 4.7 Integration Tests — Multi-Tenant Filters
```csharp
// Tests in tests/SharpCoreDB.Provider.Sync.Tests/Integration/MultiTenantSyncTests.cs
[Fact] WhenFilterByTenantId_OnlyTenantDataSynced()
[Fact] WhenMultipleTenantsOnServer_EachClientGetsOwnSubset()
```

### Deliverable
- >90% code coverage on provider code
- All integration tests pass against in-memory SharpCoreDB + SQLite
- Performance benchmark: 10K row sync < 5 seconds

---

## Phase 5: Filter Support (Week 8)

### Goal
Implement Dotmim.Sync filter integration for multi-tenant sync scenarios.

> **Note on encryption:** SharpCoreDB's AES-256-GCM encryption is at-rest only. The `CryptoService` decrypts data transparently at the storage layer (`Storage.ReadWrite.ReadBytes()`) before it reaches any API surface. The sync provider reads plaintext rows through the standard `ITable.Select()` / `ExecuteQuery()` APIs — exactly like any other consumer. **No encryption bridge, special flags, or adapter code is needed.** This is confirmed by the Phase 4.6 sanity test.

### Tasks

#### 5.1 Implement Filter Parameter Handling
- Map Dotmim.Sync `SyncParameters` to SharpCoreDB parameterized queries
- Generate filtered SELECT change queries with `WHERE {filter_column} = @filter_value`
- Verify filter columns have hash indexes for performance

#### 5.2 Filter Integration Tests
- Verify tenant-scoped sync with parameterized filters
- Test multiple simultaneous filters (tenant_id + project_id)
- Verify filter changes on subsequent syncs

### Deliverable
- Filtered sync with tenant parameters works correctly
- Performance validated with hash indexes on filter columns

---

## Phase 6: Polish & Documentation (Week 8-9)

### Goal
Production-ready NuGet add-in package, documentation, and samples.

### Tasks

#### 6.1 NuGet Package
- **Package ID:** `SharpCoreDB.Provider.Sync` (matches YesSql/other provider naming pattern)
- **Version:** `1.0.0` (independent from SharpCoreDB core versioning)
- **Assembly:** `SharpCoreDB.Provider.Sync` (matches namespace)
- **Metadata:**
  - Icon: Use shared `SharpCoreDB.jpg`
  - README: `NuGet.README.md` (linked from GitHub)
  - Tags: `sharpcoredb;dotmim.sync;sync;database;net10;csharp14;orm;provider`
  - Dependencies: 
    - `SharpCoreDB.Data.Provider` (>= 1.3.5)
    - `Dotmim.Sync.Core` (1.1.x)
    - `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.x)
- **Multi-RID:** Support `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

#### 6.2 README & Getting Started
```markdown
# SharpCoreDB.Provider.Sync

Dotmim.Sync provider for SharpCoreDB — bidirectional sync with PostgreSQL, SQL Server, SQLite, MySQL.

This add-in enables local-first AI agent architectures: sync only a subset of server data locally using filtered sync, run high-performance vector search + graph queries on the encrypted local database.

## Installation

\`\`\`bash
dotnet add package SharpCoreDB.Provider.Sync
\`\`\`

## Quick Start

### 1. Register the provider (Dependency Injection)

\`\`\`csharp
// Program.cs
services.AddSharpCoreDBSync(
    connectionString: "Path=C:\\data\\local.scdb;Password=secret"
);
\`\`\`

### 2. Use with Dotmim.Sync

\`\`\`csharp
// Get the provider from DI container
var clientProvider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
var serverProvider = new SqlSyncProvider(serverConnectionString);

var agent = new SyncAgent(clientProvider, serverProvider);
var result = await agent.SynchronizeAsync(setup);
\`\`\`

### 3. Multi-Tenant Filtering

\`\`\`csharp
var parameters = new SyncParameters((
    ("tenant_id", 42),
    ("project_id", 7)
));
var result = await agent.SynchronizeAsync(setup, parameters);
\`\`\`
```

#### 6.3 Sample Application
- Console app demonstrating:
  - Server (SQLite) ↔ Client (SharpCoreDB) sync via DI
  - Filtered sync by tenant_id
  - Conflict resolution handling
  - Performance benchmarking

#### 6.4 XML Documentation
- All public APIs have `<summary>`, `<param>`, `<returns>`, `<exception>` docs
- Performance annotations on hot paths
- DI registration examples

#### 6.5 Update Main README
- Add sync capabilities to SharpCoreDB feature list
- Link to `SharpCoreDB.Provider.Sync` NuGet package
- Include DI setup example with usage code

### Deliverable
- NuGet-ready package
- Getting-started documentation with DI examples
- Working sample application
- All public APIs documented

---

## Technical Decision Log

| # | Decision | Rationale |
|---|---|---|
| TD-1 | Use shadow tables + triggers (not WAL-based tracking) | Matches Dotmim.Sync's SQLite provider pattern; simpler; works with all storage modes |
| TD-2 | Client-side provider first (not server) | Primary use case is local-first; server-side can be added later |
| TD-3 | No encryption adapter needed | Encryption is at-rest only; data is plaintext at the API layer; sync provider uses standard read/write APIs |
| TD-4 | Long ticks for timestamps (not DateTime) | Monotonic, no timezone issues, efficient comparison, matches Dotmim.Sync convention |
| TD-5 | Separate tracking tables (not ALTER TABLE) | No schema pollution of user tables; cleaner provisioning/deprovisioning |
| TD-6 | Pin Dotmim.Sync.Core 1.1.x | Stable release; 2.0 is still preview |
| TD-7 | Follow add-in pattern (SharpCoreDB.Provider.Sync) | Consistent with YesSql/other providers; enables optional package installation; proper NuGet namespace |
| TD-8 | Use DI for provider factory | Matches Microsoft.Extensions.DependencyInjection pattern; enables service composition and integration |
| TD-9 | Use existing ADO.NET provider (SharpCoreDB.Data.Provider) | Dotmim.Sync operates through DbConnection/DbCommand; reuse existing infrastructure |

---

## Milestone Checkpoints

| Milestone | Criteria | Target |
|---|---|---|
| **M1** — Provider Compiles | `SharpCoreDBSyncProvider` instantiates; `SyncAgent` accepts it | Week 2 |
| **M2** — DI Integration Works | `services.AddSharpCoreDBSync()` registers and resolves provider from container | Week 2 |
| **M3** — Change Tracking Works | Insert/Update/Delete captured in tracking tables via triggers | Week 4 |
| **M4** — First Sync | SharpCoreDB ↔ SQLite one-way sync (server → client) succeeds | Week 5 |
| **M5** — Bidirectional Sync | Full bidirectional sync with conflict resolution | Week 6 |
| **M6** — Filtered Sync | Multi-tenant filtered sync works with parameterized filters | Week 8 |
| **M7** — Release Candidate | All tests pass (incl. encrypted DB sanity), NuGet package ready, docs complete | Week 9 |

---

## Resource Requirements

- **Developer:** 1 senior .NET developer (familiar with SharpCoreDB internals and Dotmim.Sync)
- **Reviewer:** 1 developer for PR reviews (Dotmim.Sync and DI patterns experience preferred)
- **Testing:** Access to PostgreSQL/SQL Server instances for integration testing
- **CI/CD:** GitHub Actions pipeline for automated testing and NuGet publishing

---

## Risk Register

| Risk | Probability | Impact | Mitigation | Owner |
|---|---|---|---|---|
| SharpCoreDB trigger bodies can't execute cross-table DML | Low | Critical | Verify in Phase 0, task 0.2; fallback to direct ITable API calls | Dev |
| Dotmim.Sync CoreProvider API changes in 2.0 | Medium | High | Pin to 1.1.x; abstract provider interface for future migration | Dev |
| JOIN performance degrades with large tracking tables | Medium | High | Hash index on tracking PK; benchmark in Phase 0, task 0.4; tombstone cleanup | Dev |
| Concurrent sync and local writes cause deadlocks | Low | High | Use MVCC for sync reads; batch update transactions for writes | Dev |
| SharpCoreDB parameterized query syntax incompatible | Low | Medium | Validate all query templates in Phase 1; adapt SqlParser if needed | Dev |
| DI container misconfiguration in consuming apps | Low | Medium | Comprehensive examples; XML docs; sample project | Dev |

---

## Future Roadmap (Post v1.0)

| Feature | Priority | Description |
|---|---|---|
| Zero-Knowledge Sync | High | E2E encrypted sync where server never sees plaintext |
| Server-Side Provider | Medium | Use SharpCoreDB as sync server (SharpCoreDB ↔ SharpCoreDB) |
| WebSocket Transport | Medium | Real-time sync instead of poll-based |
| Selective Sync | Medium | Column-level filtering (sync only specific columns) |
| Compression | Low | gzip/brotli batch compression for metered connections |
| Offline Queue | Low | Queue changes while offline; auto-sync on reconnect |
| Vector Data Sync | Low | Sync vector embeddings for local AI inference |
| Graph Data Sync | Low | Sync graph edges/nodes for local graph queries |
