# SQLite & PostgreSQL Compatibility Matrix — SharpCoreDB v1.7.0

> **Last updated:** v1.7.0  
> **Goal:** SharpCoreDB must equal SQLite on every user-facing feature.  
> SharpCoreDB extensions (`STORAGE=`, `SYNC_TIMESTAMP()`, `ULID`, native Vector index, etc.) are *extra* — they never replace missing SQLite coverage.

---

## Legend

| Symbol | Meaning |
|---|---|
| ✅ | Fully supported |
| ⚠️ | Partially supported — read Notes |
| ❌ | Not yet supported (gap to close) |
| ➕ | SharpCoreDB extension beyond SQLite/PostgreSQL |

---

## 1 · DDL (CREATE / DROP / ALTER)

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `CREATE TABLE` | ✅ | ✅ | ✅ | |
| `CREATE TABLE IF NOT EXISTS` | ✅ | ✅ | ✅ | |
| `CREATE TABLE … STORAGE=` | ❌ | ❌ | ➕ | COLUMNAR / PAGE_BASED / HYBRID |
| Column types: INTEGER / TEXT / REAL / BLOB / NUMERIC | ✅ | ✅ | ✅ | |
| Column types: DECIMAL / BOOLEAN / DATETIME / VECTOR | ❌ | ✅ | ➕ | SharpCoreDB extensions |
| `PRIMARY KEY` inline | ✅ | ✅ | ✅ | |
| `PRIMARY KEY` table constraint | ✅ | ✅ | ⚠️ | Parsed, honoured as index |
| `AUTOINCREMENT` / `INTEGER PRIMARY KEY` auto-rowid | ✅ | ✅ | ✅ | last_insert_rowid() supported |
| `NOT NULL` constraint | ✅ | ✅ | ✅ | |
| `DEFAULT` literal value | ✅ | ✅ | ✅ | |
| `DEFAULT` expression (e.g. `DEFAULT (strftime(…))`) | ✅ | ✅ | ⚠️ | Parsed, but expression not evaluated at insert time |
| `UNIQUE` column constraint | ✅ | ✅ | ✅ | |
| `UNIQUE` table constraint | ✅ | ✅ | ⚠️ | Parsed, stored, enforcement varies |
| `CHECK` constraint | ✅ | ✅ | ⚠️ | Parsed, not enforced at insert/update time |
| `FOREIGN KEY … REFERENCES` | ✅ | ✅ | ⚠️ | Parsed and stored; no cascade enforcement yet |
| `COLLATE NOCASE / RTRIM / BINARY` | ✅ | ✅ | ✅ | Phase 3 collation implemented |
| `COLLATE LOCALE_*` | ❌ | ✅ | ➕ | SharpCoreDB locale collation extension |
| `CREATE INDEX` | ✅ | ✅ | ✅ | |
| `CREATE UNIQUE INDEX` | ✅ | ✅ | ✅ | |
| `CREATE INDEX IF NOT EXISTS` | ✅ | ✅ | ✅ | |
| `CREATE VECTOR INDEX` | ❌ | ❌ | ➕ | ANN search extension |
| `DROP TABLE` | ✅ | ✅ | ✅ | |
| `DROP TABLE IF EXISTS` | ✅ | ✅ | ✅ | |
| `DROP INDEX` | ✅ | ✅ | ✅ | |
| `ALTER TABLE ADD COLUMN` | ✅ | ✅ | ✅ | |
| `ALTER TABLE DROP COLUMN` | ✅ | ✅ | ✅ | |
| `ALTER TABLE RENAME TO` | ✅ | ✅ | ✅ | |
| `ALTER TABLE RENAME COLUMN` | ✅ | ✅ | ✅ | |
| `CREATE VIEW` | ✅ | ✅ | ✅ | |
| `CREATE MATERIALIZED VIEW` | ❌ | ✅ | ⚠️ | Routes to standard view internally |
| `DROP VIEW` | ✅ | ✅ | ✅ | |
| `CREATE TRIGGER` AFTER/BEFORE INSERT/UPDATE/DELETE | ✅ | ✅ | ✅ | NEW.* / OLD.* references |
| `DROP TRIGGER` | ✅ | ✅ | ✅ | |
| `VACUUM` | ✅ | ❌ | ✅ | |
| `PRAGMA table_info` / `index_list` / `foreign_key_list` | ✅ | ❌ | ✅ | |
| `PRAGMA journal_mode` / `synchronous` | ✅ | ❌ | ⚠️ | Parsed, limited effect |

---

## 2 · DML (INSERT / UPDATE / DELETE

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `INSERT INTO … VALUES (…)` | ✅ | ✅ | ✅ | |
| `INSERT INTO … (cols) VALUES (…)` | ✅ | ✅ | ✅ | |
| `INSERT INTO … SELECT …` | ✅ | ✅ | ❌ | Gap |
| `INSERT OR REPLACE` | ✅ | ❌ | ✅ | **New in v1.7.0** — supports primary key, single-column `UNIQUE`, named `UNIQUE INDEX`, and composite `UNIQUE` conflicts |
| `INSERT OR IGNORE` | ✅ | ❌ | ✅ | **New in v1.7.0** — supports primary key, single-column `UNIQUE`, named `UNIQUE INDEX`, and composite `UNIQUE` conflicts |
| `INSERT OR FAIL` | ✅ | ❌ | ✅ | **New in v1.7.0** — throws on conflict and preserves earlier rows from the same multi-row `VALUES` statement |
| `INSERT OR ABORT` | ✅ | ❌ | ✅ | **New in v1.7.0** — throws on conflict and rolls back earlier rows from the same multi-row `VALUES` statement |
| `UPSERT: … ON CONFLICT DO NOTHING` | ✅ | ✅ | ✅ | **New in v1.7.0** — supports primary key, inline UNIQUE, named UNIQUE INDEX, composite UNIQUE, and optional target-column clause |
| `UPSERT: … ON CONFLICT DO UPDATE SET …` | ✅ | ✅ | ✅ | **New in v1.7.0** — supports conflict-target columns, `excluded.col` expressions, literal assignments, and optional `WHERE` filter |
| `UPSERT: … ON CONFLICT DO UPDATE SET … WHERE …` | ✅ | ✅ | ✅ | **New in v1.7.0** — update executes only when WHERE predicate matches conflicting row |
| `RETURNING` clause | ✅ | ✅ | ❌ | Gap |
| `UPDATE … SET …` | ✅ | ✅ | ✅ | |
| `UPDATE … WHERE …` | ✅ | ✅ | ✅ | |
| `UPDATE … FROM …` (PostgreSQL-style) | ❌ | ✅ | ❌ | Not needed for SQLite parity |
| `DELETE FROM …` | ✅ | ✅ | ✅ | |
| `DELETE FROM … WHERE …` | ✅ | ✅ | ✅ | |
| Parameterised queries `@param` / `?` | ✅ | ✅ | ✅ | |
| `ExecuteBatchSQL` (batch of statements) | ❌ | ❌ | ➕ | Native performance extension |

---

## 3 · SELECT & Query features

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `SELECT *` | ✅ | ✅ | ✅ | |
| `SELECT col, col AS alias` | ✅ | ✅ | ✅ | |
| `SELECT DISTINCT` | ✅ | ✅ | ✅ | |
| `WHERE` with `=`, `!=`, `<`, `<=`, `>`, `>=` | ✅ | ✅ | ✅ | |
| `AND` / `OR` / `NOT` | ✅ | ✅ | ✅ | |
| `IS NULL` / `IS NOT NULL` | ✅ | ✅ | ✅ | |
| `BETWEEN … AND …` | ✅ | ✅ | ✅ | |
| `IN (…)` / `NOT IN (…)` | ✅ | ✅ | ✅ | |
| `LIKE` (% and _ wildcards) | ✅ | ✅ | ✅ | **New in v1.7.0** — scalar function-form `LIKE(value, pattern)` supported in literal SELECT path |
| `GLOB` | ✅ | ❌ | ✅ | Implemented v1.7.0 |
| `REGEXP` | ✅ extension | ✅ | ✅ | **New in v1.7.0** — AST WHERE evaluation supports `REGEXP` / `NOT REGEXP` |
| `ORDER BY col [ASC\|DESC]` | ✅ | ✅ | ✅ | |
| `ORDER BY ordinal position` (e.g. `ORDER BY 2`) | ✅ | ✅ | ✅ | |
| Multi-column `ORDER BY` | ✅ | ✅ | ✅ | **New in v1.7.0** — AstExecutor chains all sort keys |
| `LIMIT` / `OFFSET` | ✅ | ✅ | ✅ | |
| `INNER JOIN … ON …` | ✅ | ✅ | ✅ | |
| `LEFT JOIN` | ✅ | ✅ | ✅ | |
| `RIGHT JOIN` | ❌ | ✅ | ✅ | SharpCoreDB extends SQLite here |
| `FULL OUTER JOIN` | ❌ | ✅ | ✅ | SharpCoreDB extends SQLite here |
| `CROSS JOIN` | ✅ | ✅ | ✅ | |
| Multi-table JOIN chain | ✅ | ✅ | ✅ | |
| Subquery in `WHERE` (`IN (SELECT …)`) | ✅ | ✅ | ✅ | |
| Correlated subquery | ✅ | ✅ | ⚠️ | Basic support via SubqueryExecutor |
| Subquery in `FROM` (derived table) | ✅ | ✅ | ⚠️ | Routes to EnhancedSqlParser |
| Subquery in `SELECT` column list | ✅ | ✅ | ✅ | |
| `WITH` CTE (non-recursive) | ✅ | ✅ | ⚠️ | Parsed, basic execution |
| `WITH RECURSIVE` CTE | ✅ | ✅ | ❌ | Gap |
| `CASE WHEN … THEN … ELSE … END` | ✅ | ✅ | ⚠️ | Evaluated in AST executor; not in legacy DML path |
| `CAST(… AS type)` | ✅ | ✅ | ⚠️ | TypeConverter handles common casts |
| `COALESCE(…)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `IFNULL(a, b)` / `NULLIF(a, b)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `IIF(cond, t, f)` | ✅ | ❌ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `TYPEOF(x)` | ✅ | ❌ | ✅ | **New in v1.7.0** — SQLite type names (`integer`, `real`, `text`, `blob`, `null`) |
| `EXISTS (subquery)` / `NOT EXISTS` | ✅ | ✅ | ⚠️ | Partial |
| `UNION` / `UNION ALL` | ✅ | ✅ | ✅ | Implemented v1.7.0 |
| `INTERSECT` / `EXCEPT` | ✅ | ✅ | ✅ | Implemented v1.7.0 |
| `EXPLAIN` / `EXPLAIN QUERY PLAN` | ✅ | ✅ | ⚠️ | Text output; not structured rows |

---

## 4 · Aggregates

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `COUNT(*)` | ✅ | ✅ | ✅ | |
| `COUNT(col)` (skip NULLs) | ✅ | ✅ | ✅ | |
| `COUNT(DISTINCT col)` | ✅ | ✅ | ✅ | |
| `SUM(col)` | ✅ | ✅ | ✅ | |
| `AVG(col)` | ✅ | ✅ | ✅ | |
| `MIN(col)` / `MAX(col)` | ✅ | ✅ | ✅ | |
| `SUM(expr)` e.g. `SUM(price * qty)` | ✅ | ✅ | ✅ | **New in v1.7.0** |
| `AVG(expr)` / `MIN(expr)` / `MAX(expr)` | ✅ | ✅ | ✅ | **New in v1.7.0** |
| `GROUP BY` single column | ✅ | ✅ | ✅ | |
| `GROUP BY` multiple columns | ✅ | ✅ | ✅ | |
| `HAVING` clause | ✅ | ✅ | ✅ | **New in v1.7.0** |
| `FILTER (WHERE …)` per aggregate | ✅ | ✅ | ✅ | |
| `FILTER` on a JOIN-backed aggregate | ✅ | ✅ | ✅ | **New in v1.7.0** |
| `GROUP_CONCAT(col)` / `STRING_AGG` | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor + SqlFunctions |
| `group_concat(col, sep)` | ✅ | ❌ | ✅ | **New in v1.7.0** — custom separator supported |
| `TOTAL(col)` (SQLite: returns 0.0 not NULL) | ✅ | ❌ | ✅ | **New in v1.7.0** — returns 0.0 on empty, never NULL |
| Ordered-set: `percentile_cont WITHIN GROUP` | ❌ | ✅ | ❌ | PostgreSQL-only; not a parity gap |
| `FILTER` on window functions | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |

---

## 5 · Window Functions (`… OVER (…)`)

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `ROW_NUMBER() OVER (…)` | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |
| `RANK() OVER (…)` | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |
| `DENSE_RANK() OVER (…)` | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |
| `LAST_VALUE(col) OVER (…)` | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |
| `PARTITION BY` multiple columns | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |
| `ORDER BY` with ASC/DESC | ✅ | ✅ | ✅ | **New in v1.7.0** — SQL executor with AstExecutor |

---

## 6 · Scalar Functions

| Function | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `ABS(x)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `ROUND(x, n)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `CEIL(x)` / `FLOOR(x)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `MAX(a,b)` / `MIN(a,b)` (scalar two-arg) | ✅ | ❌ | ❌ | Gap |
| `LENGTH(s)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `SUBSTR(s, start, len)` | ✅ | ✅ | ✅ | **New in v1.7.0** — 1-based SQLite semantics |
| `UPPER(s)` / `LOWER(s)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `TRIM(s)` / `LTRIM(s)` / `RTRIM(s)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `REPLACE(s, from, to)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `INSTR(s, sub)` | ✅ | ❌ | ✅ | **New in v1.7.0** — SQLite-compatible 1-based index |
| `LIKE(s, pattern)` | ✅ | ✅ | ✅ | **New in v1.7.0** — scalar function-form supported |
| `HEX(x)` / `UNHEX(x)` | ✅ | ❌ | ✅ | `HEX(x)` and `UNHEX(x)` both supported |
| `QUOTE(x)` | ✅ | ❌ | ✅ | **New in v1.7.0** — returns SQL literal text with proper single-quote escaping |
| `RANDOM()` | ✅ | ✅ | ✅ | Implemented v1.7.0 |
| `COALESCE(a, b, …)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `IFNULL(a, b)` | ✅ | ❌ | ✅ | **New in v1.7.0** — SQLite alias of COALESCE |
| `NULLIF(a, b)` | ✅ | ✅ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `IIF(cond, t, f)` | ✅ | ❌ | ✅ | **New in v1.7.0** — AST scalar function dispatch |
| `TYPEOF(x)` | ✅ | ❌ | ✅ | **New in v1.7.0** — SQLite type names (`integer`, `real`, `text`, `blob`, `null`) |
| `CHANGES()` / `TOTAL_CHANGES()` | ✅ | ❌ | ⚠️ | Stubbed in AST path only; full engine semantics still pending |
| `LAST_INSERT_ROWID()` | ✅ | ❌ | ✅ | |
| `CURRENT_TIMESTAMP` | ✅ | ✅ | ✅ | |
| `CURRENT_DATE` / `CURRENT_TIME` | ✅ | ✅ | ✅ | |
| `DATE(timestring, modifier…)` | ✅ | ❌ | ⚠️ | Basic; modifiers not all implemented |
| `DATETIME(timestring, modifier…)` | ✅ | ❌ | ⚠️ | Basic |
| `STRFTIME(format, timestring)` | ✅ | ❌ | ⚠️ | format maps to .NET |
| `JULIANDAY(timestring)` | ✅ | ❌ | ❌ | Gap |
| `NOW()` | ❌ | ✅ | ✅ | SharpCoreDB adds this for PostgreSQL parity |
| `SYNC_TIMESTAMP()` | ❌ | ❌ | ➕ | Native sync extension |
| `ULID()` / `ULID_NEW()` | ❌ | ❌ | ➕ | Native extension |
| `VEC_DISTANCE_*(…)` | ❌ | ❌ | ➕ | Vector search extension |
| `CHAR(n, …)` / `UNICODE(s)` | ✅ | ✅ | ✅ | **New in v1.7.0** — CHAR builds strings from code points; UNICODE returns first code point (supports supplementary chars) |

---

## 7 · Transactions & Concurrency

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `BEGIN` / `COMMIT` / `ROLLBACK` | ✅ | ✅ | ✅ | Via WAL |
| `BEGIN IMMEDIATE` / `BEGIN EXCLUSIVE` | ✅ | ❌ | ❌ | Gap |
| `SAVEPOINT name` / `RELEASE` / `ROLLBACK TO` | ✅ | ✅ | ❌ | Gap |
| MVCC (Multiversion Concurrency Control) | ❌ | ✅ | ✅ | Native MVCC engine |
| WAL (Write-Ahead Log) | ✅ | ✅ | ✅ | |
| Optimistic concurrency / version columns | ❌ | ❌ | ➕ | Native extension |

---

## 8 · Stored Procedures & Views

| Feature | SQLite | PostgreSQL | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|---|
| `CREATE PROCEDURE` / `EXEC` | ❌ | ✅ | ✅ | SharpCoreDB extends SQLite here |
| `DROP PROCEDURE` | ❌ | ✅ | ✅ | |
| `CREATE VIEW` | ✅ | ✅ | ✅ | |
| Updatable views | ✅ | ✅ | ❌ | Gap |

---

## 9 · Indexing (SharpCoreDB extensions)

| Feature | SQLite | SharpCoreDB v1.7.0 |
|---|---|---|
| B-Tree index (standard) | ✅ | ✅ |
| Hash index | ❌ | ➕ |
| Expression index | ❌ | ➕ |
| Vector index (ANN / HNSW) | ❌ | ➕ |
| Columnar storage engine | ❌ | ➕ |
| Page-based storage engine | ❌ | ➕ |
| Hybrid storage engine | ❌ | ➕ |

---

## 10 · Meta / System

| Feature | SQLite | SharpCoreDB v1.7.0 | Notes |
|---|---|---|---|
| `sqlite_master` / `sqlite_schema` query | ✅ | ✅ | |
| `PRAGMA table_info(name)` | ✅ | ✅ | |
| `PRAGMA index_list(name)` | ✅ | ✅ | |
| `PRAGMA foreign_key_list(name)` | ✅ | ✅ | |
| `PRAGMA integrity_check` | ✅ | ❌ | Gap |
| `PRAGMA journal_mode=WAL` | ✅ | ❌ | Parsed; no runtime switch |
| `EXPLAIN QUERY PLAN` (structured rows) | ✅ | ✅ | ⚠️ Plain text output only |

---

## 11 · Gaps priority list (SQLite parity)

These are the most impactful missing features for full SQLite compatibility, ordered by user impact:

| Priority | Feature | Gap type |
|---|---|---|
| 🔴 P0 | `COALESCE` / `IFNULL` scalar functions | Missing scalar function |
| 🔴 P0 | `INSERT OR REPLACE` / `INSERT OR IGNORE` | Missing DML variant |
| ✅ Done | `UNION` / `UNION ALL` / `INTERSECT` / `EXCEPT` | Implemented v1.7.0 |
| 🟠 P1 | `WITH RECURSIVE` (recursive CTE) | Missing CTE variant |
| ✅ Done | `GLOB` operator | Implemented v1.7.0 |
| 🟠 P1 | String functions: `LIKE()` function-form, `UNHEX`, `QUOTE`, `CHAR`, `UNICODE` | Missing scalar functions |
| ✅ Done | Numeric functions: scalar `MAX/MIN`, `POW/POWER`, `SQRT`, `MOD`, `RANDOM` | Implemented v1.7.0 |
| 🟡 P2 | `CHANGES()` / `TOTAL_CHANGES()` full semantics | Missing scalar function semantics |
| 🟡 P2 | `CHECK` constraint enforcement at DML time | Constraint enforcement |
| 🟡 P2 | `SAVEPOINT` / `RELEASE` / `ROLLBACK TO` | Missing transaction feature |
| 🟡 P2 | `RETURNING` clause | Missing DML feature |
| 🟢 P3 | `NTILE` / `PERCENT_RANK` / `CUME_DIST` window functions | Window function coverage |
| 🟢 P3 | `JULIANDAY` / full `strftime` modifiers | Date function coverage |
| 🟢 P3 | `INSERT INTO … SELECT …` | Missing DML form |
| 🟢 P3 | `EXPLAIN QUERY PLAN` structured row output | Diagnostic feature |

---

## 12 · SharpCoreDB-native extensions (beyond SQLite)

These are capabilities SharpCoreDB **adds on top** of SQLite — not parity gaps, but competitive advantages:

| Extension | Description |
|---|---|
| `STORAGE = COLUMNAR \| PAGE_BASED \| HYBRID` | Per-table storage engine selection |
| Hash index | O(1) lookup for equality predicates |
| Expression index | Index on computed column expressions |
| Vector index (ANN/HNSW) | `VEC_DISTANCE_*()` ORDER BY + LIMIT optimisation |
| MVCC engine | Multi-version concurrency without table locks |
| `SYNC_TIMESTAMP()` | Monotonic UTC tick for sync change tracking |
| `ULID()` | Time-sortable unique ID generation |
| `ExecuteBatchSQL` | Atomic batched write path with single flush |
| Native collation: `LOCALE_*` | ICU-quality locale-aware string ordering |
| Event sourcing package | Optional ES/CQRS append-only table mode |
| gRPC server mode | Network-accessible SharpCoreDB server |
| RIGHT JOIN / FULL OUTER JOIN | Extended beyond SQLite's join support |
| Stored Procedures (`CREATE PROCEDURE`) | SQLite does not support stored procedures |
