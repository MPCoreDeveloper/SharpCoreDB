# SQLite & PostgreSQL Compatibility — Work Checklist

> **Goal:** Full SQLite syntax parity + key PostgreSQL additions in SharpCoreDB.  
> **Reference matrix:** `docs/compatibility/SQLITE_POSTGRESQL_AGGREGATE_SYNTAX_v1.7.0.md`  
> **Last updated:** v1.7.0 — 2025-07-03 (W3-5 DEFAULT expression marked done; AstExecutor VisitSetOperation implemented)

---

## Legend

- `[x]` Done / merged
- `[ ]` Not started
- `[~]` In progress
- `[-]` Skipped / deferred

---

## 🟢 Quick Wins (~1 day total — START HERE)

> High user visibility, very low effort. All in `SqlFunctions.cs` + AstExecutor expression dispatch.

- [x] `COALESCE(a, b, …)` scalar function
- [x] `IFNULL(a, b)` scalar function
- [x] `NULLIF(a, b)` scalar function
- [x] `ABS(x)` numeric function
- [x] `ROUND(x, n)` numeric function
- [x] `CEIL(x)` / `CEILING(x)` numeric function
- [x] `FLOOR(x)` numeric function
- [x] `UPPER(s)` / `LOWER(s)` string functions
- [x] `LENGTH(s)` string function
- [x] `TRIM(s)` / `LTRIM(s)` / `RTRIM(s)` string functions
- [x] `IIF(cond, t, f)` scalar function
- [x] `TYPEOF(x)` scalar function
- [x] `REGEXP` operator (WHERE col REGEXP pattern)
- [x] Multi-column `ORDER BY` fix (beyond first column only)

---

## 🔴 Wave 1 — P0 Critical (~4 weeks)

### W1-1 · NULL-safety functions
- [x] `COALESCE` registered in parser (already done via quick wins — verify EF Core path)
- [x] `IFNULL` alias works in AST execution path
- [x] Verify legacy DML path parity for `COALESCE` / `IFNULL` / `NULLIF`

### W1-2 · INSERT conflict variants
- [x] Parse `INSERT OR REPLACE INTO …`
- [x] Parse `INSERT OR IGNORE INTO …`
- [x] Parse `INSERT OR FAIL INTO …`
- [x] Parse `INSERT OR ABORT INTO …`
- [x] Execute conflict policy: REPLACE on primary key conflict (delete + reinsert)
- [x] Execute conflict policy: IGNORE on primary key conflict (skip on duplicate)
- [x] Extend REPLACE / IGNORE semantics to single-column inline `UNIQUE` constraints
- [x] Extend REPLACE / IGNORE semantics to named single-column `UNIQUE INDEX` conflicts
- [x] Extend REPLACE / IGNORE semantics to composite UNIQUE conflicts
- [x] Execute FAIL semantics (throw on conflict, preserve earlier rows in same multi-row statement)
- [x] Execute ABORT semantics (throw on conflict, roll back earlier rows in same multi-row statement)
- [x] Tests for primary-key `INSERT OR REPLACE` / `INSERT OR IGNORE`
- [x] Tests for non-PK single-column `UNIQUE` / `UNIQUE INDEX` conflict paths
- [x] Tests for composite `UNIQUE` conflict paths
- [x] Tests for `INSERT OR FAIL` / `INSERT OR ABORT`

### W1-3 · UPSERT (`ON CONFLICT`)
- [x] Parse `ON CONFLICT (col_list) DO NOTHING`
- [x] Parse `ON CONFLICT (col_list) DO UPDATE SET …`
- [x] Parse `ON CONFLICT (col_list) DO UPDATE SET … WHERE …`
- [x] Execute DO NOTHING path (primary key, inline UNIQUE, named UNIQUE INDEX, composite UNIQUE, optional target-column)
- [x] Execute DO UPDATE path (update matching row)
- [x] Wire into EF Core `SharpCoreDBMigrationsSqlGenerator` test
- [x] Tests: upsert inserts when no conflict
- [x] Tests: upsert updates when conflict on PK
- [x] Tests: upsert updates when conflict on UNIQUE column
- [x] Tests: DO NOTHING inserts when no conflict
- [x] Tests: DO NOTHING skips on PK conflict
- [x] Tests: DO NOTHING skips on UNIQUE column conflict
- [x] Tests: DO NOTHING respects optional target-column clause

---

## 🟠 Wave 2 — P1 High Impact (~8 weeks)

### W2-1 · Set operations
- [x] Parse `UNION` between two SELECT arms
- [x] Parse `UNION ALL`
- [x] Parse `INTERSECT`
- [x] Parse `EXCEPT`
- [x] Add `SetOperationNode` to `SqlAst.Nodes.cs`
- [x] Execute UNION (deduplicate)
- [x] Execute UNION ALL (no dedup)
- [x] Execute INTERSECT
- [x] Execute EXCEPT
- [x] `ORDER BY` / `LIMIT` on outer set result
- [x] Tests for all four set operations

### W2-2 · String scalar functions (extended)
- [x] `SUBSTR(s, start)` — 1-based index, SQLite convention
- [x] `SUBSTR(s, start, len)`
- [x] `SUBSTRING(s, start, len)` — PostgreSQL alias
- [x] `REPLACE(s, from, to)`
- [x] `INSTR(s, sub)`
- [x] `LIKE(s, pattern)` — function form
- [x] `HEX(x)`
- [x] `UNHEX(x)`
- [x] `QUOTE(x)`
- [x] `CHAR(n, …)` / `UNICODE(s)`
- [x] Tests for implemented string functions

### W2-3 · Numeric scalar functions (extended)
- [x] `MAX(a, b)` — 2-arg scalar form (distinct from aggregate MAX)
- [x] `MIN(a, b)` — 2-arg scalar form
- [x] `POW(x, y)` / `POWER(x, y)`
- [x] `SQRT(x)`
- [x] `MOD(x, y)` / `%` operator
- [x] `SIGN(x)`
- [x] `RANDOM()` — returns random integer (SQLite: 64-bit signed)
- [x] Tests for implemented numeric functions

### W2-4 · GLOB operator
- [x] Parse `col GLOB pattern` in expression parser
- [x] Implement glob→regex conversion (`*`→`.*`, `?`→`.`, case-sensitive)
- [x] Support character classes `[A-Z]`
- [x] Tests for GLOB with various patterns

### W2-5 · `WITH RECURSIVE` CTE
- [ ] Detect `RECURSIVE` keyword in `WITH` clause
- [ ] Parse anchor SELECT arm
- [ ] Parse recursive SELECT arm (`UNION ALL`)
- [ ] Implement iterative execution loop (seed → iterate until empty)
- [ ] Add cycle detection / LIMIT guard
- [ ] Tests: recursive hierarchy traversal
- [ ] Tests: counting sequence with RECURSIVE

### W2-6 · Multi-column ORDER BY (already done in quick wins — verify)
- [x] Fix comparator to chain all sort keys with ASC/DESC

---

## 🟡 Wave 3 — P2 Medium Impact (~4 weeks)

### W3-1 · CHECK constraint enforcement
- [ ] At INSERT time: evaluate CHECK expressions via AstExecutor
- [ ] At UPDATE time: evaluate CHECK expressions
- [ ] Raise `ConstraintViolationException` on failure
- [ ] Tests: table with CHECK constraint rejects invalid INSERT
- [ ] Tests: table with CHECK constraint rejects invalid UPDATE

### W3-2 · SAVEPOINT
- [ ] Parse `SAVEPOINT name`
- [ ] Parse `RELEASE SAVEPOINT name`
- [ ] Parse `ROLLBACK TO SAVEPOINT name`
- [ ] Implement savepoint stack over WAL layer
- [ ] Snapshot WAL position on SAVEPOINT
- [ ] Revert to snapshot on ROLLBACK TO
- [ ] Tests for nested savepoints

### W3-3 · RETURNING clause
- [ ] Parse `INSERT … RETURNING col_list`
- [ ] Parse `UPDATE … RETURNING col_list`
- [ ] Parse `DELETE … RETURNING col_list`
- [ ] Evaluate RETURNING expressions against affected rows
- [ ] Return as result set (not row count)
- [ ] Tests: `INSERT … RETURNING id`
- [ ] Tests: `UPDATE … RETURNING *`

### W3-4 · IIF / TYPEOF / CHANGES (extended coverage)
- [x] `IIF(cond, t, f)`
- [x] `TYPEOF(x)`
- [ ] `CHANGES()` — returns row-change count of last DML statement
- [ ] `TOTAL_CHANGES()` — cumulative change count
- [ ] `LAST_INSERT_ROWID()` — verify works in all execution paths

### W3-5 · DEFAULT expression evaluation
- [x] At INSERT time, evaluate `DEFAULT (expr)` for omitted columns
- [x] Support `DEFAULT (strftime('%Y-%m-%d', 'now'))`
- [x] Tests for DEFAULT expression columns

### W3-6 · Correlated subquery (full support)
- [ ] Pass outer row context into inner executor on each iteration
- [ ] Support `EXISTS (SELECT … WHERE outer.col = inner.col)`
- [ ] Support `NOT EXISTS` correlated
- [ ] Tests: correlated subquery in WHERE
- [ ] Tests: correlated subquery in SELECT column list

### W3-7 · UNIQUE / FOREIGN KEY enforcement hardening
- [ ] Enforce UNIQUE on INSERT
- [ ] Enforce UNIQUE on UPDATE
- [ ] Parse and track FOREIGN KEY references
- [ ] ON DELETE CASCADE execution
- [ ] ON DELETE SET NULL execution
- [ ] Tests for all constraint paths

---

## 🟢 Wave 4 — P3 Polish (~3 weeks)

### W4-1 · Additional window functions
- [ ] `LAG(col, n)` / `LEAD(col, n)` — verify fully implemented
- [ ] `FIRST_VALUE(col)` / `LAST_VALUE(col)` — verify
- [ ] `NTILE(n)` window function
- [ ] `PERCENT_RANK()` window function
- [ ] `CUME_DIST()` window function
- [ ] `FRAME` clause: `ROWS BETWEEN … AND …`
- [ ] `RANGE BETWEEN … AND …`

### W4-2 · INSERT INTO … SELECT …
- [ ] Parse SELECT arm in INSERT
- [ ] Pipe result rows into INSERT executor
- [ ] Tests: INSERT INTO target SELECT * FROM source

### W4-3 · Date/time functions (full coverage)
- [ ] `JULIANDAY(timestring)`
- [ ] `UNIXEPOCH(timestring)` (SQLite 3.38+)
- [ ] `strftime` modifiers: `'+N days'`, `'start of month'`, `'weekday N'`
- [ ] `DATE(timestring, modifier…)` full modifier support
- [ ] `DATETIME(timestring, modifier…)` full modifier support
- [ ] `TIME(timestring, modifier…)`

### W4-4 · REGEXP operator (verify + extend)
- [x] Basic `REGEXP` in WHERE clause
- [ ] `NOT REGEXP`
- [ ] Case-insensitive flag support

### W4-5 · EXPLAIN QUERY PLAN structured rows
- [ ] Return tabular rows: `(id, parent, notused, detail)` matching SQLite schema
- [ ] Tests: `EXPLAIN QUERY PLAN SELECT …` returns structured result

### W4-6 · BEGIN IMMEDIATE / BEGIN EXCLUSIVE
- [ ] Parse `BEGIN IMMEDIATE`
- [ ] Parse `BEGIN EXCLUSIVE`
- [ ] Map to appropriate WAL locking semantics

### W4-7 · Updatable views
- [ ] `INSERT INTO view …` delegates to base table
- [ ] `UPDATE view …` delegates to base table
- [ ] `DELETE FROM view …` delegates to base table

---

## 📊 Progress Tracker

| Wave | Total items | Done | Remaining |
|---|---|---|---|
| Quick Wins | 14 | 14 | 0 |
| W1 — P0 | 31 | 31 | 0 |
| W2 — P1 | 42 | 35 | 7 |
| W3 — P2 | 38 | 5 | 33 |
| W4 — P3 | 27 | 1 | 26 |
| **Total** | **152** | **86** | **66** |

---

## 🏗️ Key files to modify

| File | Purpose |
|---|---|
| `src/SharpCoreDB/Services/SqlFunctions.cs` | All scalar function implementations |
| `src/SharpCoreDB/Services/EnhancedSqlParser.Expressions.cs` | Function call dispatch, operator parsing |
| `src/SharpCoreDB/Services/EnhancedSqlParser.DML.cs` | INSERT OR / ON CONFLICT / RETURNING parsing |
| `src/SharpCoreDB/Services/EnhancedSqlParser.Select.cs` | UNION / INTERSECT / EXCEPT / WITH RECURSIVE |
| `src/SharpCoreDB/Services/SqlAst.Nodes.cs` | New AST node types |
| `src/SharpCoreDB/Services/SqlAst.Core.cs` | AstExecutor expression evaluation |
| `src/SharpCoreDB/Services/SqlAst.DML.cs` | DML execution paths |
| `tests/SharpCoreDB.Tests/` | Tests for all new features |
| `docs/compatibility/SQLITE_POSTGRESQL_AGGREGATE_SYNTAX_v1.7.0.md` | Keep in sync |
