# 6. Querying

> SQL dialect, aggregates, window functions, joins, subqueries, and query APIs.
> Deep dives: [`docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`](../sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md) ·
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
| `UpdateMultiple` / `DeleteMultiple` | — | — | bulk updates/deletes |

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

See [`docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`](../sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md).

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
