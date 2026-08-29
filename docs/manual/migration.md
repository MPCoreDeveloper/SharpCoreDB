# 12. Migration to SharpCoreDB

> Migrating existing data and code from other engines. Deep dives:
> [`docs/migration/README.md`](../migration/README.md) ·
> [`docs/migration/MIGRATION_GUIDE.md`](../migration/MIGRATION_GUIDE.md) ·
> [`docs/migration/SQLITE_VECTORS_TO_SHARPCORE.md`](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)

---

## 12.1 From SQLite

- **SQL dialect**: SharpCoreDB implements a SQLite-compatible dialect subset — `INTEGER PRIMARY
  KEY` rowid alias, `_rowid`, `COLLATE`, `INSERT OR REPLACE`-style semantics — so most DDL/DML
  ports with minimal changes.
- **Data**: use the built-in migration tooling in `docs/migration/MIGRATION_GUIDE.md` to copy
  tables and rows programmatically.
- **Vectors**: replace SQLite vector extensions with `SharpCoreDB.VectorSearch`
  ([`SQLITE_VECTORS_TO_SHARPCORE.md`](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)).
- **Performance**: switch hot read loops to `FindByPrimaryKey` / `ExecuteQueryStruct` and hot
  ingestion to `InsertBatch` — see the [Performance Guide](performance.md).

## 12.2 From LiteDB

- Replace BSON document access with typed SQL tables and rows.
- `LiteCollection<T>.FindById` → `db.FindByPrimaryKey(table, key)`.
- `InsertBulk` → `db.InsertBatch`.
- You gain SQL, encryption, vector search, and analytics you did not have.

## 12.3 From RavenDB / MongoDB (network engines)

- Embedded-first: use `SharpCoreDB` directly, or `SharpCoreDB.Server` for the gRPC network
  layer.
- RLS replaces per-document authorization filters.
- Document-centric schemas map cleanly to `TEXT`/`BLOB` columns with a ULID PK.

## 12.4 FluentMigrator

Add versioned migrations to any SharpCoreDB database with the `SharpCoreDB.FluentMigrator`
package — see [Providers](providers.md#106-migrations-sharpcoredbfluentmigrator).

## 12.5 Check list

1. Port schema → `CREATE TABLE` (keep `INTEGER PRIMARY KEY` / ULID patterns for keys).
2. Port data via a scripted copy (per-table `ExecuteQuery` + `InsertBatch`).
3. Re-create indexes (`CREATE INDEX`, hash for point lookups, B-tree for ranges).
4. Switch hot loops to the v2.0 fast-path APIs.
5. Run the comparative benchmark to confirm target numbers on your machine.
