# 6. Querying

> SQL dialect, aggregates, window functions, joins, subqueries, and query APIs.
> [`docs/internals/SUBQUERY_IMPLEMENTATION.md`](../internals/SUBQUERY_IMPLEMENTATION.md) ·
> [`docs/internals/JOIN_IMPLEMENTATION.md`](../internals/JOIN_IMPLEMENTATION.md) ·
> [`docs/QUERY_PLAN_CACHE.md`](../QUERY_PLAN_CACHE.md)

---

## 6.1 Query APIs (pick by performance need)

| API | Allocates? | SQL support | Best for |
|-----|-----------|-------------|----------|
| `ExecuteQuery(sql, params)` | per-row `Dictionary` | full | interactive/adhoc, dynamic columns |
| ⚡ `ExecuteQueryStruct(sql, params)` | **zero-alloc** | full | hot read loops — the v2.0 fast path |
| ⚡ `FindByPrimaryKey(table, key)` / `FindByIndex(table, col, value)` | per-row `Dictionary` | — | **Direct API**: no SQL parsing, fastest point reads |
| `ExecuteSQL(sql)` | — | DML/DDL | writes |
| `Insert(table, row)` / `InsertBatch(table, rows)` | — | — | single-row / bulk writes (see Performance Guide) |
| `InsertBatch(table, rows, columns)` | — | — | dictionary-free bulk writes: column-ordered `object[]` rows (1,10–1,25× the dictionary overload) |
| `UpdateBatch(table, ops)` / `DeleteBatch(table, keys)` | — | — | bulk updates/deletes with typed keys — no SQL text (1,4–2,0× `ExecuteBatchSQL` on the measured shapes) |

## 6.2 SELECT examples

```sql
-- Point lookup with parameter
SELECT * FROM customers WHERE email = @email;

-- Range + sort using B-tree index
SELECT id, created, total
FROM orders
WHERE created >= @from AND created <= @to
ORDER BY created DESC
LIMIT 100;

-- IN list
SELECT * FROM customers WHERE id IN (10, 20, 30);

-- Aggregation
SELECT status, COUNT(*) AS n, SUM(total) AS sum, AVG(total) AS avg
FROM orders
GROUP BY status
ORDER BY n DESC;
```

## 6.3 Aggregates (100+) & window functions

- **Scalar aggregates:** COUNT, SUM, AVG, MIN, MAX, STDDEV, VARIANCE, PERCENTILE_CONT/DISC,
  MEDIAN, CORRELATION, COVAR, first/last, string aggregates (`GROUP_CONCAT`), … 
- **Window functions:** ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD, FIRST_VALUE,
  LAST_VALUE, running SUM/AVG, frame clauses (`ROWS BETWEEN …`)

```sql
SELECT id, status, total,
       RANK()       OVER (PARTITION BY status ORDER BY total DESC) AS rank_in_status,
       LAG(total)   OVER (PARTITION BY status ORDER BY created)   AS prev_total,
       SUM(total)   OVER (PARTITION BY status)                    AS status_total
FROM orders;
```

## 6.4 Joins & subqueries

```sql
-- INNER / LEFT / RIGHT / FULL / CROSS
SELECT c.name, o.total
FROM customers c
LEFT JOIN orders o ON o.customer = c.id;

-- Derived table
SELECT * FROM (SELECT customer, SUM(total) s FROM orders GROUP BY customer) WHERE s > 100;

-- CTE
WITH RECURSIVE org(id, path) AS (
  SELECT id, CAST(id AS TEXT) FROM employees WHERE manager IS NULL
  UNION ALL
  SELECT e.id, org.path || '>' || e.id FROM employees e JOIN org ON e.manager = org.id
)
SELECT * FROM org;
```

## 6.5 SQL dialect extensions

SharpCoreDB adds engine-specific keywords:

| Extension | Meaning |
|-----------|---------|
| `CREATE TABLE … STORAGE = COLUMNAR` | per-table columnar storage |
| `OPTIONALLY` | optional SQL option clauses (skips full parse on hot paths) |
| `RETURNING`-style result helpers | post-write row access |
| `_rowid`, `PRIMARY KEY AUTO` | hidden ULID row id + monotonic integer auto-increment |
| `COLLATE` everywhere | per-expression collation |
| `INSERT … ON CONFLICT DO NOTHING / DO UPDATE` | upsert |


## 6.6 Query plan cache & prepared statements

- Every parsed SQL statement is cached in `QueryPlanCache` keyed by **normalized SQL**
  (parameters replaced). v2.0 added a regex-free normalizer and an allocation short-circuit.
- `PreparedStatements` — `db.Prepare(sql)` compiles once (returns a `PreparedStatement`);
  `db.ExecutePrepared(stmt, params)` and `db.ExecuteCompiledQuery(stmt, params)` execute it many
  times. The recommended pattern for hot loops.
- v2.0 `SimpleSelectPlan` performs a **zero-reparse SELECT fast path**: simple
  `SELECT * FROM t WHERE key = @p` plans are resolved from the cache without re-lexing.

> ⚡ **Guidance:** parameterize everything, reuse `IDatabase` instances, and keep working-set
> statements under the cache size. See [`docs/QUERY_PLAN_CACHE.md`](../docs/QUERY_PLAN_CACHE.md).

## 6.7 `information_schema` metadata views

The engine answers the SQL-standard `information_schema` views directly from the live schema, so the
embedded API, the REST endpoint, the WebSocket endpoint and the PostgreSQL binary protocol all see
the same metadata. `sqlite_master` and `PRAGMA` are untouched — this is additive capability.

| View | Rows | Notes |
|---|---|---|
| `information_schema.tables` | base tables and views | views carry `table_type = 'VIEW'`; 12 PostgreSQL-compatible columns |
| `information_schema.columns` | one row per column of every base table | `ordinal_position`, `column_default`, `is_nullable` and `data_type` are populated; the remaining standard columns are `NULL`; views are not listed (their columns come from an arbitrary SELECT) |
| `information_schema.schemata` | `public`, `information_schema`, `pg_catalog` | |
| `information_schema.views` | registered views | `view_definition` holds the SELECT text |
| `information_schema.triggers` | registered triggers | timing, event, table and body |
| `information_schema.indexes` | hash, B-tree and vector indexes | SharpCoreDB-specific (PostgreSQL uses `pg_indexes`); `index_type` is `HASH`, `BTREE`, `FLAT`, `HNSW` or `DISKANN` |

```sql
SELECT table_schema, table_name FROM information_schema.tables LIMIT 5;
SELECT index_name, column_name, index_type FROM information_schema.indexes WHERE table_name = 'documents';
SELECT COUNT(*) FROM information_schema.tables;
```

Supported statement shapes: explicit column lists with `AS` aliases, `*`, `WHERE` (`=`, `<>`, `!=`,
`<`, `<=`, `>`, `>=`, `LIKE`, `NOT LIKE`, `IN`, `NOT IN`, `IS [NOT] NULL`, `AND`/`OR`, parentheses),
`ORDER BY` (multi-column, `ASC`/`DESC`, ordinals), `LIMIT`/`OFFSET` and `COUNT(*)` — which returns the
engine's usual `cnt` column. `table_catalog` reports the database name.

Nothing is guessed. A predicate on a column the view does not expose, a projection using an
expression, and an unknown view (`information_schema.foo`) all throw instead of answering the wrong
question. The constraint and routine views (`table_constraints`, `key_column_usage`,
`referential_constraints`, `constraint_column_usage`, `check_constraints`, `routines`, `parameters`)
are recognised and answer **empty** with their standard column list — the engine keeps no catalog for
them yet.

Covered by `tests/SharpCoreDB.Tests/InformationSchemaTests.cs` and by the REST metadata-discovery
step of `tests/CompatibilitySmoke`.
