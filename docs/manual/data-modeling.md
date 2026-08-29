# 4. Data Modeling

> Types, keys, constraints, collation, and schema management. Deep dives:
> [`docs/features/AUTO_ROWID.md`](../features/AUTO_ROWID.md) ·
> [`docs/collation/COLLATION_GUIDE.md`](../collation/COLLATION_GUIDE.md) ·
> [`docs/serialization/README.md`](../serialization/README.md)

---

## 4.1 Data types

| SQL type | .NET type | Notes |
|----------|-----------|-------|
| `TEXT` | `string` | UTF-8, collation-aware |
| `INTEGER` | `int` | 32-bit; `INTEGER PRIMARY KEY` acts as rowid alias (SQLite-compatible) |
| `LONG` | `long` | 64-bit |
| `REAL` | `double` | IEEE-754 |
| `DECIMAL` | `decimal` | 28-digit fixed point |
| `BOOLEAN` | `bool` | stored as 0/1 |
| `DATETIME` | `DateTime` | UTC, tick precision |
| `GUID` | `Guid` | 16-byte |
| `ULID` | `Ulid` | 16-byte sortable ID |
| `BLOB` | `byte[]` | binary payloads |
| `ROWREF` | internal | row reference/pointer |

## 4.2 Primary keys & auto-generated IDs

```sql
-- SQLite-style rowid alias (INTEGER PRIMARY KEY)
CREATE TABLE t1 (id INTEGER PRIMARY KEY, name TEXT);

-- Integer auto-increment (persisted across restarts)
CREATE TABLE t2 (id INTEGER PRIMARY KEY AUTO, name TEXT);

-- Hidden _rowid: when a table has NO explicit PRIMARY KEY,
-- SharpCoreDB injects a hidden _rowid (ULID, AUTO, PRIMARY KEY, NOT NULL)
-- exactly like SQLite's rowid — but ULID-based instead of monotonic integer:
CREATE TABLE t3 (name TEXT, email TEXT);   -- _rowid ULID auto-PK added automatically

-- Explicit ULID key (set values yourself, or leave null to auto-generate)
CREATE TABLE t4 (id ULID PRIMARY KEY, name TEXT);
```

- `_rowid` is a hidden auto-generated primary key of type **ULID** (`Ulid.NewUlid()`) — sortable by
  time, collision-free across nodes, and index-friendly.
- `id INTEGER PRIMARY KEY AUTO` gives the classic monotonic integer counter, persisted across
  restarts.
- Explicit `GUID`/`ULID` columns auto-generate their value when inserted with a null/missing key.

See [`docs/features/AUTO_ROWID.md`](../features/AUTO_ROWID.md).

## 4.3 Constraints

```sql
CREATE TABLE orders (
  id        LONG PRIMARY KEY,
  customer  LONG NOT NULL,
  total     REAL DEFAULT 0,
  status    TEXT CHECK (status IN ('open','closed')),
  created   DATETIME DEFAULT current_timestamp,
  UNIQUE (customer, created)
);
```

Foreign keys, `ON DELETE`/`ON UPDATE` cascade rules, and deferrable constraints are supported.

## 4.4 Collation

| Collation | Behavior |
|-----------|----------|
| `BINARY` | byte-wise (default, fastest) |
| `NOCASE` | ASCII case-insensitive |
| `RTRIM` | ignores trailing spaces |
| `UNICODE_CI` | Unicode case-insensitive |
| `LOCALE("xx_XX")` | ICU/OS locale-aware |

```sql
CREATE TABLE users (name TEXT COLLATE NOCASE);
SELECT * FROM users WHERE name = 'ADA' COLLATE NOCASE;  -- matches 'ada'
```

> ⚡ `BINARY` collation is the only one that can be matched via pure SIMD byte compares.
> Prefer it on hot lookup columns. See [`docs/collation/COLLATION_GUIDE.md`](../collation/COLLATION_GUIDE.md).

## 4.5 Schema management APIs

- `db.CreateTable(...)`, `db.DropTable(...)`, `db.RenameTable(...)`, `db.AlterTable(...)`
- `db.AddColumn`, `db.DropColumn`, `db.RenameColumn`
- `db.GetTableMetadata(name)` for column/index introspection
- `db.GetTables()` enumerates the catalog
