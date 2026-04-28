# SingleFileDatabase SQL Limitations

**Applies to:** SharpCoreDB v1.8.x — `.scdb` single-file mode (`StorageMode.SingleFile`)

> **TL;DR:** `SingleFileDatabase` uses the full `SqlParser` engine for **DML and SELECT** statements.
> **DDL** (`CREATE TABLE` / `DROP TABLE`) uses an internal regex path because the directory-mode DDL engine
> operates on `Table` instances that are not compatible with `SingleFileTable`.
> For advanced DDL features (storage modes, COLUMNAR, complex indexes) use the **Directory mode** (`Database` class).

---

## Architecture Overview

SharpCoreDB exposes one `IDatabase` interface with two concrete implementations:

| Class | Mode | File extension | SQL engine (DML/SELECT) | SQL engine (DDL) |
|---|---|---|---|---|
| `Database` | Directory | `.db` folder | `SqlParser` — full featured | `SqlParser.DDL.cs` |
| `SingleFileDatabase` | Single-file | `.scdb` | `SqlParser` (shared, v1.8+) | Regex path in `SingleFileDatabase` |

### Why the DDL split?

`SqlParser.DDL.cs` creates tables by calling `ITableFactory.CreateTable(...)` and then sets
directory-mode `Table` properties (`DataFile`, `StorageMode`, `InitializeStorageEngine()`, etc.).
`SingleFileTable` has a different lifecycle managed by `SingleFileStorageProvider` and
`TableDirectoryManager` and is incompatible with that post-creation setup flow.

DDL operations in single-file mode are therefore handled by `ExecuteCreateTableInternal` and
`ExecuteDropTableInternal` inside `SingleFileDatabase`, which register schema changes correctly
with the `.scdb` file format.

---

## SQL Support Matrix

### DDL (CREATE / DROP)

| Statement | Supported | Notes |
|---|---|---|
| `CREATE TABLE` | ✅ | Quoted identifiers supported (v1.7.2+) |
| `CREATE TABLE IF NOT EXISTS` | ✅ | |
| `DROP TABLE` | ✅ | Quoted identifiers supported (v1.7.2+) |
| `DROP TABLE IF EXISTS` | ✅ | |
| `CREATE INDEX` | ⚠️ silently ignored | No-op — returns without error |
| `DROP INDEX` | ⚠️ silently ignored | No-op — returns without error |
| `CREATE TRIGGER` | ⚠️ silently ignored | No-op — returns without error |
| `DROP TRIGGER` | ⚠️ silently ignored | No-op — returns without error |
| `CREATE VIEW` | ❌ | Throws `InvalidOperationException` |
| `ALTER TABLE` | ❌ | Throws `InvalidOperationException` |
| Column types | ✅ | INTEGER, TEXT, REAL, DECIMAL, DATETIME, BLOB, BOOLEAN, LONG, GUID/UUID |
| `PRIMARY KEY`, `NOT NULL`, `AUTOINCREMENT` | ✅ | |
| `UNIQUE` column constraint | ✅ | |
| `DEFAULT` with comma-containing expressions | ✅ | Quote-aware splitter (v1.7.2+) |
| `FOREIGN KEY` | ⚠️ silently parsed/skipped | No enforcement |
| `CHECK` constraint | ⚠️ silently parsed/skipped | No enforcement |
| `STORAGE = COLUMNAR` | ❌ | DDL regex path does not support storage mode hints |

### DML — INSERT / UPDATE / DELETE

All DML is routed through the shared `SqlParser`, which means the same feature set as directory mode:

| Feature | Supported | Notes |
|---|---|---|
| `INSERT INTO t VALUES (...)` | ✅ | |
| `INSERT INTO t (cols) VALUES (...)` | ✅ | |
| `INSERT OR IGNORE` | ✅ | |
| `INSERT OR REPLACE` | ✅ | |
| Multi-row `INSERT INTO t VALUES (...),(...)` | ✅ | |
| `UPDATE t SET col = val WHERE ...` | ✅ | |
| `UPDATE` without `WHERE` | ✅ | Updates all rows |
| Expressions in SET (`col = col + 1`) | ✅ | |
| `DELETE FROM t WHERE ...` | ✅ | |
| `DELETE FROM t` (no WHERE) | ✅ | Deletes all rows |
| Parameterized queries (`@param`, `?`) | ✅ | |

### SELECT — Query

All SELECT is routed through the shared `SqlParser`:

| Feature | Supported | Notes |
|---|---|---|
| `SELECT * FROM t` | ✅ | |
| `SELECT col1, col2 FROM t` | ✅ | |
| `WHERE` with `=`, `<>`, `<`, `>`, `<=`, `>=` | ✅ | |
| `WHERE` with `AND` / `OR` | ✅ | |
| `ORDER BY col ASC/DESC` | ✅ | |
| `LIMIT` / `OFFSET` | ✅ | |
| `INNER JOIN` | ✅ | |
| `LEFT JOIN` | ✅ | |
| `RIGHT JOIN` / `FULL OUTER JOIN` | ✅ | Via EnhancedSqlParser |
| `GROUP BY` | ✅ | |
| `HAVING` | ✅ | |
| Aggregate functions (`COUNT`, `SUM`, `MIN`, `MAX`, `AVG`) | ✅ | |
| `DISTINCT` | ✅ | |
| Column aliases (`col AS alias`) | ✅ | |
| `IS NULL` / `IS NOT NULL` | ✅ | |
| `LIKE` | ✅ | |
| `IN (...)` | ✅ | |
| Subqueries in `FROM` / `WHERE` | ✅ | Via EnhancedSqlParser |
| `EXPLAIN` | ✅ | |

### Transactions

| Feature | Supported | Notes |
|---|---|---|
| `BEGIN` / `COMMIT` / `ROLLBACK` | ⚠️ silently ignored | Single-file storage auto-flushes |
| `SAVEPOINT` | ❌ | Not supported |

---

## Practical Impact: FluentMigrator

FluentMigrator generates SQL that `SingleFileDatabase` can handle for all typical migrations:

| FluentMigrator operation | Works in `.scdb`? |
|---|---|
| `Create.Table(...)` | ✅ (quoted identifiers supported v1.7.2+) |
| `Delete.Table(...)` | ✅ |
| `Insert.IntoTable(...).Row(...)` | ✅ |
| `Update.Table(...).Set(...).Where(...)` | ✅ |
| `Delete.FromTable(...).Row(...)` | ✅ |
| `Delete.FromTable(...)` (no condition) | ✅ (v1.8+ via SqlParser) |
| `Create.Index(...)` | ⚠️ silently ignored |
| `Create.ForeignKey(...)` | ⚠️ silently ignored |
| Version table creation (`__SharpMigrations`) | ✅ |

---

## Choosing the Right Mode

```
Need advanced DDL (COLUMNAR storage mode, complex CREATE INDEX, ALTER TABLE)?
    → Use Directory mode (Database class, .db folder)

Need encrypted portable single-file storage with full DML/SELECT support?
    → Use SingleFile mode (SingleFileDatabase, .scdb)
    → Full INSERT/UPDATE/DELETE/SELECT via SqlParser
    → Standard CREATE TABLE / DROP TABLE via regex (all common DDL supported)
```

### Directory mode

```csharp
// Full SqlParser for DDL + DML + SELECT
var db = factory.Create("myapp", "password");  // creates myapp/ folder
var results = db.ExecuteQuery("SELECT u.name, o.total FROM users u INNER JOIN orders o ON u.id = o.user_id");
```

### Single-file mode

```csharp
// Full SqlParser for DML/SELECT; regex DDL for CREATE/DROP TABLE
var db = factory.CreateWithOptions("myapp.scdb", "password", DatabaseOptions.CreateSingleFileDefault());

// All of these work in single-file mode (v1.8+):
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice'), (2, 'Bob')");
db.ExecuteSQL("UPDATE users SET name = 'Alicia' WHERE id = 1");
db.ExecuteSQL("DELETE FROM users WHERE id = 2");

var results = db.ExecuteQuery("SELECT * FROM users WHERE name LIKE 'A%' ORDER BY id LIMIT 10");
var joined  = db.ExecuteQuery("SELECT u.name, o.total FROM users u JOIN orders o ON u.id = o.user_id");
```

---

## Error Messages You Might See

| Error | Cause |
|---|---|
| `InvalidOperationException: Invalid CREATE TABLE syntax` | Malformed DDL (quoted identifiers before v1.7.2 — fixed) |
| `InvalidOperationException: Invalid DROP TABLE syntax` | Malformed DROP TABLE statement |
| `InvalidOperationException: Table 'X' not found` | Table does not exist in this `.scdb` file |
| `NotSupportedException: Query not supported in single-file mode` | Non-SELECT query passed to `ExecuteQuery` |

---

## Internal Architecture Note for Contributors

```
SingleFileDatabase.ExecuteSQL(sql)
  ├── CREATE TABLE  ──→  ExecuteCreateTableInternal()  (regex, registers with TableDirectoryManager)
  ├── DROP TABLE    ──→  ExecuteDropTableInternal()     (regex, removes from TableDirectoryManager)
  ├── CREATE/DROP INDEX/TRIGGER  ──→  silently ignored
  └── everything else  ──→  ExecuteDMLInternal()
                                └──→  GetSqlParser()  ──→  Services.SqlParser (shared engine)
                                                           (INSERT / UPDATE / DELETE / SELECT)
```

The `Services.SqlParser` used here is the DML-only constructor variant
(`SqlParser(tables, dbPath, isReadOnly, queryCache, config)`), which has `_tableFactory = null`
and will throw `ArgumentNullException` if DDL is routed to it — by design.

A factory-aware constructor (`SqlParser(tables, dbPath, ITableFactory, ...)`) also exists and can be
used by future implementations that bridge a compatible `SingleFileTableFactory` once the
`Table`/`SingleFileTable` storage lifecycle is unified.

---

## See Also

- [`docs/storage/STORAGE_MODE_GUIDANCE.md`](STORAGE_MODE_GUIDANCE.md) — choosing between Columnar and Page-Based within a mode
- [`docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`](../sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md) — full SQL dialect for Directory mode
- [`Examples/FluentMigrator/`](../../Examples/FluentMigrator/) — working FluentMigrator + SingleFileDatabase demo

---

*Last updated: v1.8.0*
