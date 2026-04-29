# SingleFileDatabase SQL Support

**Applies to:** SharpCoreDB v1.8.x+ — `.scdb` single-file mode (`StorageMode.SingleFile`)

> **TL;DR:** Both `SingleFileDatabase` (`.scdb`) and `Database` (directory mode) share the same
> `SqlParser` engine for DML and SELECT. DDL in single-file mode uses an internal regex path that
> now covers the same core operations as directory mode, including `ALTER TABLE` with
> `ADD COLUMN`, `DROP COLUMN`, `RENAME COLUMN`, `RENAME TO`, `CREATE INDEX`, and `DROP INDEX`.

---

## Architecture Overview

SharpCoreDB exposes one `IDatabase` interface with two concrete implementations:

| Class | Mode | File extension | SQL engine (DML/SELECT) | SQL engine (DDL) |
|---|---|---|---|---|
| `Database` | Directory | `.db` folder | `SqlParser` — full featured | `SqlParser.DDL.cs` |
| `SingleFileDatabase` | Single-file | `.scdb` | `SqlParser` (shared) | Regex path in `DatabaseExtensions.cs` |

Both modes support the same DDL feature set for `CREATE TABLE`, `DROP TABLE`, `ALTER TABLE`,
`CREATE INDEX`, and `DROP INDEX`. The only DDL statements not yet supported in single-file mode
are `CREATE VIEW`, `CREATE TRIGGER`, and `STORAGE = COLUMNAR` hints (directory-mode only).

---

## SQL Support Matrix

### DDL (CREATE / DROP / ALTER)

| Statement | Single-File | Directory | Notes |
|---|---|---|---|
| `CREATE TABLE` | ✅ | ✅ | Quoted identifiers supported |
| `CREATE TABLE IF NOT EXISTS` | ✅ | ✅ | |
| `DROP TABLE` | ✅ | ✅ | Quoted identifiers supported |
| `DROP TABLE IF EXISTS` | ✅ | ✅ | |
| `CREATE INDEX` | ✅ | ✅ | Tracked for `IndexExists()` probes |
| `CREATE UNIQUE INDEX` | ✅ | ✅ | |
| `DROP INDEX` | ✅ | ✅ | |
| `ALTER TABLE ... RENAME TO` | ✅ | ✅ | |
| `ALTER TABLE ... ADD COLUMN` | ✅ | ✅ | |
| `ALTER TABLE ... RENAME COLUMN ... TO` | ✅ | ✅ | Updates schema and row data |
| `ALTER TABLE ... DROP COLUMN` | ✅ | ✅ | Updates schema and row data |
| `CREATE TRIGGER` | ⚠️ silently ignored | ✅ | |
| `DROP TRIGGER` | ⚠️ silently ignored | ✅ | |
| `CREATE VIEW` | ❌ not supported | ✅ | |
| `STORAGE = COLUMNAR` | ❌ not supported | ✅ | Directory mode only |
| Column types | ✅ | ✅ | INTEGER, TEXT, REAL, DECIMAL, DATETIME, BLOB, BOOLEAN, LONG, GUID/UUID, ULID |
| `PRIMARY KEY`, `NOT NULL`, `AUTOINCREMENT` | ✅ | ✅ | |
| `UNIQUE` column constraint | ✅ | ✅ | |
| `DEFAULT` with comma-containing expressions | ✅ | ✅ | Quote-aware splitter |
| `FOREIGN KEY` | ⚠️ parsed, not enforced | ⚠️ parsed, not enforced | |
| `CHECK` constraint | ⚠️ parsed, not enforced | ⚠️ parsed, not enforced | |

### Schema Introspection (FluentMigrator / migration tools)

| Query | Single-File | Directory | Notes |
|---|---|---|---|
| `PRAGMA table_info(tableName)` | ✅ | ✅ | Returns column metadata |
| `SELECT ... FROM sqlite_master WHERE type='index'` | ✅ | ✅ | Index existence checks |
| `sqlite_schema` alias | ✅ | ✅ | Synonym for `sqlite_master` |

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

All typical FluentMigrator migration operations work identically against both modes:

| FluentMigrator operation | Works in `.scdb`? | Works in `.db`? |
|---|---|---|
| `Create.Table(...)` | ✅ | ✅ |
| `Delete.Table(...)` | ✅ | ✅ |
| `Create.Index(...)` | ✅ | ✅ |
| `Delete.Index(...)` | ✅ | ✅ |
| `Alter.Table(...).AddColumn(...)` | ✅ | ✅ |
| `Alter.Table(...).AlterColumn(...)` | ✅ | ✅ |
| `Rename.Column(...).OnTable(...)` | ✅ | ✅ |
| `Insert.IntoTable(...).Row(...)` | ✅ | ✅ |
| `Update.Table(...).Set(...).Where(...)` | ✅ | ✅ |
| `Delete.FromTable(...)` | ✅ | ✅ |
| `Create.ForeignKey(...)` | ⚠️ parsed, not enforced | ⚠️ parsed, not enforced |
| Version table creation (`__SharpMigrations`) | ✅ | ✅ |
| `Delete.FromTable(...).Row(...)` | ✅ |
| `Delete.FromTable(...)` (no condition) | ✅ (v1.8+ via SqlParser) |
| `Create.Index(...)` | ⚠️ silently ignored |
| `Create.ForeignKey(...)` | ⚠️ silently ignored |
| Version table creation (`__SharpMigrations`) | ✅ |

---

## Choosing the Right Mode

```
Need COLUMNAR storage mode, Triggers, or Views?
    → Use Directory mode (Database class, .db folder)

Need encrypted portable single-file storage?
    → Use SingleFile mode (SingleFileDatabase, .scdb)
    → Full DDL parity with directory mode for all common operations
    → Full INSERT/UPDATE/DELETE/SELECT via shared SqlParser
```

### Directory mode

```csharp
var db = factory.Create("myapp", "password");  // creates myapp/ folder
db.ExecuteSQL("ALTER TABLE users RENAME COLUMN name TO full_name");
db.ExecuteSQL("ALTER TABLE users DROP COLUMN temp_col");
```

### Single-file mode

```csharp
var db = factory.CreateWithOptions("myapp.scdb", "password", DatabaseOptions.CreateSingleFileDefault());

// All of these work identically in single-file mode:
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
db.ExecuteSQL("ALTER TABLE users ADD COLUMN email TEXT");
db.ExecuteSQL("ALTER TABLE users RENAME COLUMN name TO full_name");
db.ExecuteSQL("ALTER TABLE users DROP COLUMN email");
db.ExecuteSQL("CREATE INDEX idx_name ON users (full_name)");

var results = db.ExecuteQuery("SELECT * FROM users WHERE full_name LIKE 'A%' ORDER BY id LIMIT 10");
var joined  = db.ExecuteQuery("SELECT u.full_name, o.total FROM users u JOIN orders o ON u.id = o.user_id");
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
  ├── CREATE TABLE          ──→  ExecuteCreateTableInternal()     (regex, registers schema)
  ├── DROP TABLE            ──→  ExecuteDropTableInternal()        (regex, removes schema)
  ├── CREATE/DROP INDEX     ──→  ExecuteCreate/DropIndexInternal() (tracked in _indexRegistry)
  ├── ALTER TABLE           ──→  ExecuteAlterTableInternal()       (delegates to ITable methods)
  └── everything else       ──→  ExecuteDMLInternal()
                                  └──→  SqlParser (shared DML/SELECT engine)

Database.ExecuteSQL(sql)   [directory mode]
  └── SqlParser (full DDL + DML + SELECT, including CREATE VIEW/TRIGGER, COLUMNAR storage)
```

Both implementations delegate column mutations (`ADD COLUMN`, `DROP COLUMN`, `RENAME COLUMN`)
through the shared `ITable` interface so schema changes are handled consistently regardless of
storage backend.

---

## See Also

- [`docs/storage/STORAGE_MODE_GUIDANCE.md`](STORAGE_MODE_GUIDANCE.md) — Columnar vs Page-Based storage within a database
- [`docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`](../sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md) — full SQL dialect reference
- [`Examples/FluentMigrator/`](../../Examples/FluentMigrator/) — working FluentMigrator + SingleFileDatabase demo

---

*Last updated: v1.8.0*
