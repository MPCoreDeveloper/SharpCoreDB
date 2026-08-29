# 3. Database Core

> Storage modes, transactions, security, and metadata. Deep dives:
> [`docs/storage/README.md`](../storage/README.md) · [`docs/storage/STORAGE_MODE_GUIDANCE.md`](../storage/STORAGE_MODE_GUIDANCE.md) ·
> [`docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`](../storage/SINGLE_FILE_SQL_LIMITATIONS.md) ·
> [`docs/storage/METADATA_IMPROVEMENTS_v1.7.0.md`](../storage/METADATA_IMPROVEMENTS_v1.7.0.md)

---

## 3.1 Storage modes

### Directory mode (default)

Each table, index, and column family is stored as a file under a directory, with encryption
applied per record. Best for OLTP workloads with many tables and for debuggability.

```csharp
using var db = factory.Create(@"C:\data\appdb", masterPassword: "s3cret!");
```

### Single-file mode (`.scdb`)

A single encrypted file built from 4 KB blocks with a block allocation map, index pages, and
compressed metadata. Ideal for portable/app-store scenarios.

```csharp
using var db = factory.Create(@"C:\data\appdb.scdb", masterPassword: "s3cret!");
```

> **Limitation:** single-file mode supports a subset of SQL (no cross-file/multi-db queries).
> See [`docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`](../storage/SINGLE_FILE_SQL_LIMITATIONS.md).

### Columnar storage (per table)

`CREATE TABLE … STORAGE = COLUMNAR` stores each column in its own SIMD-friendly segment.
Analytic queries (SUM/AVG/MIN/MAX/GROUP BY) run at memory-bandwidth speed via `Vector<T>`.

### Engine mode guidance

| Workload | Recommended mode |
|----------|------------------|
| Bulk ingestion / append-heavy | Append-only engine |
| OLTP with many in-place updates | Page-based engine |
| Analytics / aggregations | Columnar storage |
| Embedded portable app | Single-file `.scdb` |

See [`docs/storage/STORAGE_MODE_GUIDANCE.md`](../storage/STORAGE_MODE_GUIDANCE.md).

## 3.2 Transactions & WAL

- Full ACID: atomicity, consistency, isolation, durability.
- Write-ahead log with **group commit** and **batched durability** — many writers coalesce into
  one fsync, which is why v2.0 INSERT throughput is competitive with SQLite.
- Crash recovery verified by dedicated test suites (`RecoveryManager`).

```csharp
using (var tx = db.BeginTransaction())
{
    db.ExecuteSQL("INSERT INTO customers (name) VALUES ('A')");
    db.ExecuteSQL("INSERT INTO customers (name) VALUES ('B')");
    tx.Commit();
}
```

`db.Flush()` forces buffered writes to disk. `db.Commit(force: true)` / `db.Commit(force: false)`
control durability batching on the hot path.

## 3.3 Encryption (AES-256-GCM)

Encryption is on by default when a `masterPassword` is supplied:

- Encrypted metadata (catalog, schema) and per-record data
- Key hierarchy: master password → KEK → DEK; per-database random salt
- Optional `NoEncryptMode = true` for unencrypted development databases

```csharp
var config = new DatabaseConfig { NoEncryptMode = true };
using var db = factory.Create(@"C:\data\devdb", masterPassword: "x", config: config);
```

> ⚡ **Performance tip:** encryption adds measurable overhead on the write path. For benchmark
> comparisons vs SQLite/LiteDB, both sides should run unencrypted (or the encrypted-write delta
> should be documented explicitly). See the [Performance Guide](performance.md).

## 3.4 Metadata & catalog

The catalog (tables, columns, indexes, statistics) is versioned and cached in memory:

- `GetTableMetadata(tablename)` returns column order, types, nullable flags, PK, indexes
- `TableMetadataDto` is **source-generated** for Native AOT (no runtime reflection)
- The v2.0 `VariableLengthSchema` cache avoids re-parsing column layouts on every read

## 3.5 Concurrency & threading model

- Multiple concurrent readers are supported; writers are serialized per database
- `ReadWriteLock` with upgradable semantics; the v2.0 `LookupPositionsUnsafe` fast path
  documents its write-lock contract explicitly
- Use `db.Flush()` + `db.Dispose()` in proper order; dispose of `IDatabase` deterministically

## 3.6 Database lifecycle helpers

| API | Purpose |
|-----|---------|
| `DatabaseFactory.Create(path, password, isReadOnly, config, securityConfig)` | Open or create a database (directory or `.scdb` single-file) |
| `factory.CreateWithOptions(path, password, DatabaseOptions)` | Advanced creation (storage mode, engine, encryption options) |
| `db.Flush()` / `db.ForceSave()` | Durability: flush buffered writes; force checkpoint |
| `db.Dispose()` / `DisposeAsync()` | Close deterministically |
| `db.GetTables()` / `db.GetColumns(table)` | Catalog introspection |
| `db.GetLastInsertRowId()` | Last inserted row id |
| `db.VacuumAsync(mode)` | Reclaim space / defragment single-file databases |
| `db.NeedsLegacyUlidMigration()` / `db.MigrateLegacyUlids()` | Upgrade pre-1.9.5 ULIDs one-time |
| `db.Prepare(sql)` / `ExecutePrepared` / `ExecuteCompiledQuery` | Prepared statement lifecycle |
| `db.ClearQueryCache()` | Drop cached query plans |

> **Backup & repair** are handled by `RepairTool` and `ScdbMigrator` for `.scdb` files
> ([`docs/scdb/PRODUCTION_GUIDE.md`](../scdb/PRODUCTION_GUIDE.md)), and by the server-mode
> backup/restore runbook
> ([`docs/server/MULTITENANT_BACKUP_RESTORE_MIGRATION_v1.7.0.md`](../server/MULTITENANT_BACKUP_RESTORE_MIGRATION_v1.7.0.md)).
