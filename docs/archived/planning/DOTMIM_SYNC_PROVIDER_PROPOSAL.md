# Dotmim.Sync Provider for SharpCoreDB — Technical Proposal

**Author:** SharpCoreDB Team  
**Date:** 2025-07  
**Status:** Draft  
**Related Issue:** [#39 — Graph/AI Synchronization Discussion](https://github.com/MPCoreDeveloper/SharpCoreDB/issues/39)

---

## 1. Executive Summary

This proposal outlines the design and implementation of a **Dotmim.Sync `CoreProvider`** for SharpCoreDB, enabling **bidirectional data synchronization** between SharpCoreDB instances and any other Dotmim.Sync-supported database (PostgreSQL, SQL Server, SQLite, MySQL, MariaDB).

**Compatibility Requirement:** SharpCoreDB must remain **100% compatible with SQLite syntax and behavior** for all operations users could perform in SQLite. We may extend beyond SQLite, but **must never support less than SQLite**. This constraint applies to the provider, change tracking, and all sync-related SQL generation.

The primary use case is **Local-First AI Agents** — a hybrid architecture where:

- **Server** (PostgreSQL/SQL Server): Holds multi-tenant global knowledge
- **Client** (SharpCoreDB): Syncs only a specific project/tenant subset locally using Dotmim.Sync filters
- **Local Inference**: The local agent runs Vector Search + Graph queries on a high-performance local subset with zero latency and full privacy

### Why Dotmim.Sync?

- **Mature .NET-native framework** — 5+ years in production, MIT-licensed
- **Provider-based architecture** — Purpose-built for adding custom database engines
- **Built-in conflict resolution** — Server-wins, client-wins, or custom merge policies
- **Filter support** — Sync subsets of data per tenant/project
- **Batching & compression** — Efficient over slow/metered connections
- **HTTPS transport** — Works over standard web APIs (ASP.NET Core middleware)

---

## 2. Architecture Overview

### 2.1 Dotmim.Sync Provider Model

Dotmim.Sync uses a symmetric **`CoreProvider`** abstraction. Each database engine implements the same interface, enabling any provider to act as either server or client:

```
┌──────────────────────────────────────────────────────┐
│                  Dotmim.Sync Engine                   │
│                                                      │
│  ┌─────────────┐   Orchestrator   ┌──────────────┐  │
│  │   Server     │ ◄──────────────► │   Client     │  │
│  │  Provider    │   (HTTP/TCP)     │  Provider    │  │
│  │ (Postgres)   │                  │ (SharpCoreDB)│  │
│  └──────┬───────┘                  └──────┬───────┘  │
│         │                                 │          │
│    ┌────▼────┐                      ┌─────▼─────┐   │
│    │ PG DB   │                      │ .scdb File│   │
│    └─────────┘                      └───────────┘   │
└──────────────────────────────────────────────────────┘
```

### 2.2 SharpCoreDB Provider Position

The `SharpCoreDBSyncProvider` will implement `CoreProvider` and act primarily as a **client-side provider**, though the architecture supports server-side usage for SharpCoreDB-to-SharpCoreDB sync scenarios.

```
┌────────────────────────────────────────────────────────────────────┐
│                    SharpCoreDBSyncProvider                          │
│                                                                    │
│  ┌──────────────────┐   ┌──────────────────┐   ┌───────────────┐  │
│  │  Metadata         │   │  Change Tracking  │   │  Bulk Ops     │  │
│  │  Manager          │   │  (Shadow Tables)  │   │  (BatchSQL)   │  │
│  │                   │   │                   │   │               │  │
│  │ • scope_info      │   │ • _tracking tbl   │   │ • BulkInsert  │  │
│  │ • scope_info_cli  │   │ • Triggers        │   │ • BulkUpdate  │  │
│  │ • sync_version    │   │ • Tombstones      │   │ • BulkDelete  │  │
│  └──────────────────┘   └──────────────────┘   └───────────────┘  │
│                                                                    │
│  ┌──────────────────┐   ┌──────────────────┐                      │
│  │  Schema Builder   │   │  Conflict Mgr    │                      │
│  │                   │   │                   │   NOTE: Encryption  │
│  │ • DDL generation  │   │ • Server-wins    │   is at-rest only.  │
│  │ • Column mapping  │   │ • Client-wins    │   Data is plaintext  │
│  │ • Type mapping    │   │ • Custom merge   │   at the API layer.  │
│  └──────────────────┘   └──────────────────┘   No bridge needed.  │
└───────────────────────────────┬────────────────────────────────────┘
                                │
                   ┌────────────▼────────────┐
                   │   SharpCoreDB Engine     │
                   │                          │
                   │ • IDatabase              │
                   │ • ITable (CRUD)          │
                   │ • IStorageEngine          │
                   │ • CryptoService (AES-GCM)│  ← transparent
                   │ • WAL / MVCC             │
                   │ • Triggers (v1.2+)       │
                   └──────────────────────────┘
```

---

## 3. SharpCoreDB Capabilities Audit

### 3.1 Features That Enable Sync

| Capability | Status | Sync Relevance |
|---|---|---|
| SQL DDL (CREATE/ALTER/DROP TABLE) | ✅ v1.0+ | Schema provisioning |
| SQL DML (INSERT/UPDATE/DELETE) | ✅ v1.0+ | Data operations |
| Triggers (BEFORE/AFTER) | ✅ v1.2+ | **Critical** — Change tracking |
| Batch operations (ExecuteBatchSQL) | ✅ v1.0+ | Bulk sync apply |
| InsertBatch() | ✅ v1.0+ | Fast bulk inserts |
| Primary Keys | ✅ v1.0+ | Row identity for sync |
| Foreign Keys | ✅ v1.0+ | Schema fidelity |
| Unique Constraints | ✅ v1.0+ | Conflict detection |
| WAL (Write-Ahead Log) | ✅ v1.0+ | Crash recovery during sync |
| MVCC (Multi-Version Concurrency Control) | ✅ v1.1+ | Concurrent sync reads |
| AES-256-GCM Encryption | ✅ v1.0+ | At-rest only — transparent to sync provider (data is plaintext at API layer) |
| ADO.NET Provider | ✅ v1.0+ | Standard data access |
| Transactions (BeginBatchUpdate) | ✅ v1.0+ | Atomic sync apply |

### 3.2 Features That Need Extension

| Gap | Effort | Description |
|---|---|---|
| Shadow/Tracking Tables | Medium | Per-table `_tracking` tables with update_scope_id, timestamp, sync_row_is_tombstone |
| Change Enumeration | Medium | Efficient query: "give me all rows changed since timestamp X" |
| Timestamp Column | Low | Auto-maintained `last_modified_at` (BIGINT) column via triggers |
| Bulk Update/Delete | Low | Batch UPDATE/DELETE by primary key list |
| GUID/ULID Mapping Validation | Low | `Guid` and `Ulid` types already exist; verify SQLite-compatible storage and DbType mappings |
| Schema Introspection | Low | Programmatic access to table schema (already available via ITable) |

---

## 4. Detailed Design

### 4.1 Change Tracking Strategy

Dotmim.Sync requires the provider to track **which rows changed** since the last sync. SharpCoreDB doesn't natively have row-level change tracking, so we implement it using **shadow tracking tables + triggers** — the same approach used by SQLite and MySQL Dotmim.Sync providers.

#### 4.1.1 Tracking Table Schema

For each user table (e.g., `customers`), a shadow table `customers_tracking` is created:

```sql
CREATE TABLE customers_tracking (
    pk_customer_id INTEGER NOT NULL,
    update_scope_id TEXT,
    timestamp BIGINT NOT NULL,
    sync_row_is_tombstone INTEGER NOT NULL DEFAULT 0,
    last_change_datetime TEXT NOT NULL,
    PRIMARY KEY (pk_customer_id)
)
```

#### 4.1.2 Change Tracking Triggers

Three triggers per tracked table capture all DML changes:

```sql
-- After INSERT: record new row in tracking
CREATE TRIGGER trg_customers_insert_tracking AFTER INSERT ON customers
BEGIN
    INSERT INTO customers_tracking
    VALUES (NEW.customer_id, NULL, CURRENT_TIMESTAMP_TICKS, 0, CURRENT_DATETIME)
END

-- After UPDATE: update tracking timestamp
CREATE TRIGGER trg_customers_update_tracking AFTER UPDATE ON customers
BEGIN
    UPDATE customers_tracking
    SET timestamp = CURRENT_TIMESTAMP_TICKS,
        update_scope_id = NULL,
        last_change_datetime = CURRENT_DATETIME
    WHERE pk_customer_id = OLD.customer_id
END

-- After DELETE: mark as tombstone in tracking
CREATE TRIGGER trg_customers_delete_tracking AFTER DELETE ON customers
BEGIN
    UPDATE customers_tracking
    SET timestamp = CURRENT_TIMESTAMP_TICKS,
        sync_row_is_tombstone = 1,
        update_scope_id = NULL,
        last_change_datetime = CURRENT_DATETIME
    WHERE pk_customer_id = OLD.customer_id
END
```

> **Note:** SharpCoreDB's trigger system (v1.2+) supports `AFTER INSERT/UPDATE/DELETE` with `NEW.*` and `OLD.*` references, which is exactly what's needed. The trigger body execution via `SqlParser.FireTriggers()` will handle the tracking table writes.

#### 4.1.3 Change Enumeration Query

During sync, the provider queries changed rows since the last sync point:

```sql
SELECT c.*, ct.update_scope_id, ct.timestamp, ct.sync_row_is_tombstone
FROM customers c
INNER JOIN customers_tracking ct ON c.customer_id = ct.pk_customer_id
WHERE ct.timestamp > @last_sync_timestamp
  AND (ct.update_scope_id IS NULL OR ct.update_scope_id != @scope_id)
```

This requires SharpCoreDB's existing `JOIN` support and parameterized queries.

### 4.2 Scope Management

Dotmim.Sync uses "scopes" to track sync state between endpoints. Two metadata tables are required:

#### scope_info (Server-side)

```sql
CREATE TABLE scope_info (
    sync_scope_name TEXT NOT NULL PRIMARY KEY,
    sync_scope_schema TEXT,
    sync_scope_setup TEXT,
    sync_scope_version TEXT,
    sync_scope_last_server_sync_timestamp BIGINT,
    sync_scope_last_clean_timestamp BIGINT,
    sync_scope_properties TEXT
)
```

#### scope_info_client (Client-side)

```sql
CREATE TABLE scope_info_client (
    sync_scope_id TEXT NOT NULL,
    sync_scope_name TEXT NOT NULL,
    sync_scope_hash TEXT,
    sync_scope_parameters TEXT,
    scope_last_sync_timestamp BIGINT,
    scope_last_server_sync_timestamp BIGINT,
    scope_last_sync_duration BIGINT,
    scope_last_sync TEXT,
    scope_properties TEXT,
    PRIMARY KEY (sync_scope_id, sync_scope_name)
)
```

### 4.3 Type Mapping

SharpCoreDB `DataType` ↔ Dotmim.Sync/DbType mapping:

| SharpCoreDB DataType | .NET Type | DbType | Notes |
|---|---|---|---|
| `INTEGER` | `long` | `DbType.Int64` | Primary keys, foreign keys |
| `REAL` | `double` | `DbType.Double` | Floating point |
| `TEXT` | `string` | `DbType.String` | Variable-length strings |
| `BLOB` | `byte[]` | `DbType.Binary` | Binary data, encrypted columns |
| `BOOLEAN` | `bool` | `DbType.Boolean` | Via INTEGER 0/1 |
| `DATETIME` | `DateTime` | `DbType.DateTime` | ISO 8601 TEXT storage |
| `ULID` | `string` | `DbType.String` | 26-char Crockford Base32 |
| `GUID` | `Guid` | `DbType.Guid` | Sync scope IDs |
| `DECIMAL` | `decimal` | `DbType.Decimal` | Financial data |
| `BIGINT` | `long` | `DbType.Int64` | Timestamps |

### 4.4 Provider Class Hierarchy

```
Dotmim.Sync.CoreProvider (abstract)
  └── SharpCoreDBSyncProvider
        ├── SharpCoreDBScopeInfoBuilder      — Scope metadata CRUD
        ├── SharpCoreDBTableBuilder           — Table DDL + tracking setup
        ├── SharpCoreDBDatabaseBuilder        — Database-level provisioning
        ├── SharpCoreDBSyncAdapter            — Per-table DML (select changes, apply changes)
        └── SharpCoreDBMetadata               — Schema introspection
```

### 4.5 Key Implementation Classes

#### SharpCoreDBSyncProvider

```csharp
/// <summary>
/// Dotmim.Sync provider for SharpCoreDB encrypted database engine.
/// Enables bidirectional synchronization between SharpCoreDB and any Dotmim.Sync-supported database.
/// </summary>
public sealed class SharpCoreDBSyncProvider : CoreProvider
{
    public required string ConnectionString { get; init; }

    // CoreProvider overrides for SharpCoreDB-specific implementations
    public override DbConnection CreateConnection() => new SharpCoreDBConnection(ConnectionString);
    public override string GetDatabaseName() => /* extract from connection string */;
    // ... additional overrides
}
```

#### SharpCoreDBSyncAdapter

```csharp
/// <summary>
/// Per-table sync adapter that handles change enumeration and application.
/// Uses SharpCoreDB's trigger-based change tracking and batch operations.
/// </summary>
internal sealed class SharpCoreDBSyncAdapter
{
    // Select changes since last sync timestamp
    // Apply incoming inserts/updates/deletes with conflict detection
    // Bulk operations using ITable.InsertBatch() and ExecuteBatchSQL
}
```

---

## 5. Encryption & Sync

### 5.1 Encryption-at-Rest Is Transparent — No Special Handling Required

SharpCoreDB's AES-256-GCM encryption is **at-rest only**. Decryption happens automatically at the storage layer (`Storage.ReadWrite.ReadBytes()`) before data ever reaches the API surface. By the time the sync provider reads rows through `ITable.Select()`, `ExecuteQuery()`, or the ADO.NET `SharpCoreDBDataReader`, the data is already **plaintext in memory**.

This means:
- **No encryption bridge is needed** — the sync provider is just another consumer of the standard read APIs
- **No `noEncrypt` flags or special modes** — the provider reads data the same way any application code does
- **Writing works the same way** — data written via `Insert()`, `ExecuteSQL()`, etc. is automatically encrypted before hitting disk

```
┌──────────────────────────────────────────────────────────────┐
│                    Runtime (all in plaintext)                 │
│                                                              │
│  Sync Provider ──► ITable.Select() ──► plaintext rows        │
│  Sync Provider ──► ITable.Insert() ──► plaintext rows        │
│                          │                                   │
│                          ▼                                   │
│            CryptoService (encrypt/decrypt)                    │
│                          │                                   │
│                          ▼                                   │
│               Disk (.scdb file) ── encrypted at rest          │
└──────────────────────────────────────────────────────────────┘
```

**In-transit encryption** is handled by the Dotmim.Sync transport layer (HTTPS/TLS), which is independent of the provider implementation.

### 5.2 Zero-Knowledge Sync (Future Consideration)

A separate, advanced scenario where the **server never sees plaintext** could be explored in the future. This would involve reading raw encrypted blobs (`noEncrypt: true`) and syncing them as opaque `BLOB` columns. However, this introduces significant complexity (server can't index, compare, or resolve conflicts on encrypted data) and is **out of scope for v1.0**.

See the [Future Roadmap](#future-roadmap-post-v10) for details.

---

## 6. Filter Support (Multi-Tenant Sync)

Dotmim.Sync supports **filtered sync** — syncing only a subset of data. This is critical for the "Local-First AI Agent" use case.

### Example: Sync Only One Tenant's Data

```csharp
// Server-side setup with filter
var serverProvider = new SqlSyncProvider(connectionString);
var setup = new SyncSetup("customers", "orders", "products");

// Filter: Only sync tenant_id = 42
setup.Filters.Add("customers", "tenant_id");
setup.Filters.Add("orders", "tenant_id");

// Client-side (SharpCoreDB)
var clientProvider = new SharpCoreDBSyncProvider
{
    ConnectionString = "Path=C:\\data\\local.scdb;Password=secret"
};

var agent = new SyncAgent(clientProvider, serverProvider);
var parameters = new SyncParameters(("tenant_id", 42));
var result = await agent.SynchronizeAsync(setup, parameters);
```

The SharpCoreDB provider will generate filtered change queries:

```sql
SELECT c.*, ct.update_scope_id, ct.timestamp
FROM customers c
INNER JOIN customers_tracking ct ON c.customer_id = ct.pk_customer_id
WHERE ct.timestamp > @last_sync_timestamp
  AND c.tenant_id = @tenant_id
```

---

## 7. Performance Considerations

### 7.1 Batch Operations

The provider leverages SharpCoreDB's optimized batch paths:

| Operation | SharpCoreDB API | Expected Perf |
|---|---|---|
| Bulk Insert | `ITable.InsertBatch()` | 5-10x faster than row-by-row |
| Bulk Update | `ExecuteBatchSQL()` | Single WAL flush |
| Bulk Delete | `ExecuteBatchSQL()` | Single WAL flush |
| Change Enum | `ExecuteQuery()` with JOIN | Hash index on tracking PK |

### 7.2 Memory Efficiency

- Use `ArrayPool<byte>` for sync batch buffers
- Stream large batch files instead of loading into memory
- Leverage `Span<T>` / `ReadOnlyMemory<byte>` for binary data columns

### 7.3 Conflict Resolution Performance

- Hash indexes on tracking table PKs for O(1) conflict lookups
- B-tree indexes on timestamp columns for efficient range scans
- MVCC snapshot reads during sync to avoid blocking writers

---

## 8. Project Structure

```
src/
  SharpCoreDB.Provider.Sync/                 ← Add-in project (like SharpCoreDB.Provider.YesSql)
    SharpCoreDB.Provider.Sync.csproj
    SharpCoreDBSyncProvider.cs               ← CoreProvider implementation
    Builders/
      SharpCoreDBDatabaseBuilder.cs          ← Database provisioning
      SharpCoreDBTableBuilder.cs             ← Table + tracking setup
      SharpCoreDBScopeInfoBuilder.cs         ← Scope metadata CRUD
    Adapters/
      SharpCoreDBSyncAdapter.cs              ← Per-table change ops
      SharpCoreDBObjectNames.cs              ← SQL command text
    Metadata/
      SharpCoreDBDbMetadata.cs               ← Type mapping
      SharpCoreDBSchemaReader.cs             ← Schema introspection
    ChangeTracking/
      ChangeTrackingManager.cs               ← Trigger setup/teardown
      TrackingTableBuilder.cs                ← Shadow table DDL
      TombstoneManager.cs                    ← Deleted row tracking
    Extensions/
      SyncServiceCollectionExtensions.cs     ← DI registration (AddSharpCoreDBSync)
      SyncProviderFactory.cs                 ← Factory pattern for instantiation

tests/
  SharpCoreDB.Provider.Sync.Tests/           ← Test add-in project
    SharpCoreDB.Provider.Sync.Tests.csproj
    ChangeTrackingTests.cs
    TypeMappingTests.cs
    ConflictResolutionTests.cs
    FilteredSyncTests.cs
    Integration/
      BasicSyncIntegrationTests.cs           ← SharpCoreDB ↔ SQLite roundtrip
      MultiTenantSyncTests.cs
```
---

## 9. Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Dotmim.Sync.Core` | 1.1.x | Core sync engine abstractions |
| `SharpCoreDB` | 1.3.5+ | Database engine |
| `SharpCoreDB.Data.Provider` | 1.3.5+ | ADO.NET `DbConnection` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.x | DI registration |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.x | Structured logging |

---

## 10. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Trigger body execution via string SQL may be fragile for complex tracking | Medium | Use simple INSERT/UPDATE statements; extensive test coverage |
| No native JOIN support in tracking queries | High | SharpCoreDB already supports JOINs (JoinExecutor); verify performance with large tracking tables |
| GUID/UUID type not natively supported as DataType | Low | Store as TEXT; add GUID DataType in future release |
| Schema migration during sync setup alters user tables | Medium | Use separate tracking tables only; never modify user table schema |
| Change enumeration performance on large tables | High | Hash index on tracking PK; B-tree on timestamp; periodic tombstone cleanup |
| Dotmim.Sync API changes between versions | Low | Pin to stable 1.1.x release; integration tests catch breaks |
| SQLite compatibility gaps discovered | High | Maintain compatibility matrix; prioritize parity for all SQLite syntax used by sync |
| DI container misconfiguration in consuming apps | Low | Comprehensive examples; XML documentation; sample project demonstrates proper setup |

---

## 11. Success Criteria

1. **Bidirectional Sync**: SharpCoreDB ↔ SQLite roundtrip sync passes all Dotmim.Sync test scenarios
2. **Filtered Sync**: Tenant-scoped sync with parameterized filters works correctly
3. **Conflict Resolution**: Server-wins, client-wins, and custom merge policies function properly
4. **Encryption Transparency**: Sync works seamlessly with encrypted SharpCoreDB databases (encryption-at-rest is invisible to the provider)
5. **Performance**: Sync of 10K rows completes in < 5 seconds (including change enumeration)
6. **Reliability**: Crash recovery during sync leaves database in consistent state (WAL-protected)

---

## 12. Open Questions

1. **Dotmim.Sync version**: Should we target the latest 1.1.x stable or the preview 2.0 branch?
2. **Server-side provider**: Should SharpCoreDB also be usable as a server provider, or only client-side initially?
3. **ALTER TABLE support**: Does SharpCoreDB need `ALTER TABLE ADD COLUMN` for sync metadata, or should tracking tables be fully separate?
4. **Tombstone retention**: How long should deleted row tombstones be kept? (Dotmim.Sync recommends configurable retention)

---

## 13. References

- [Dotmim.Sync Documentation](https://dotmimsync.readthedocs.io/)
- [Dotmim.Sync GitHub](https://github.com/Mimetis/Dotmim.Sync)
- [Dotmim.Sync SQLite Provider (reference implementation)](https://github.com/Mimetis/Dotmim.Sync/tree/master/Providers/Dotmim.Sync.Sqlite)
- [SharpCoreDB Trigger System](../src/SharpCoreDB/Services/SqlParser.Triggers.cs)
- [SharpCoreDB ADO.NET Provider](../src/SharpCoreDB.Data.Provider/)
- [SharpCoreDB Issue #39](https://github.com/MPCoreDeveloper/SharpCoreDB/issues/39)
