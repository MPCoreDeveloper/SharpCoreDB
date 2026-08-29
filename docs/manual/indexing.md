# 5. Indexing

> Hash (O(1) point) and B-tree (range) indexes, plus expression/partial/adaptive variants.
> Deep dives: [`docs/internals/OPTIMIZER_ARCHITECTURE.md`](../internals/OPTIMIZER_ARCHITECTURE.md) ·
> [`docs/internals/OPTIMIZER_GUIDE.md`](../internals/OPTIMIZER_GUIDE.md)

---

## 5.1 Index types

| Index | Lookup shape | Best for |
|-------|--------------|----------|
| **Hash** | `=`, `IN` — O(1) point lookups | exact-match reads, PK/unique lookups |
| **B-tree** | `=`, `<`, `<=`, `>`, `>=`, `BETWEEN`, `ORDER BY` | range scans, sorted access |
| **Expression** | `CREATE INDEX … ON t(lower(name))` | predicates on expressions |
| **Partial** | `CREATE INDEX … WHERE status = 'active'` | hot subset filters |
| **Unique** | enforces uniqueness | constraints, idempotent loads |

## 5.2 Examples

```sql
-- Hash index (default for exact-match workloads)
CREATE INDEX idx_customers_email ON customers (email);

-- B-tree for range queries
CREATE INDEX idx_orders_created ON orders (created);

-- Expression index
CREATE INDEX idx_users_lower_email ON users (lower(email));

-- Partial index
CREATE INDEX idx_open_orders ON orders (status) WHERE status = 'open';
```

> ⚡ v2.0 `HashIndex.Add/Remove` operate on the **key only** (no full row copy) — index
> maintenance is allocation-free on the write path.

## 5.3 How the optimizer uses indexes

1. `SimpleSelectPlan` builds an **equality predicate map** (column → value).
2. If an index matches, the optimizer picks the cheapest access path:
   - **Hash index** for exact equality / `IN` lists
   - **B-tree** for ranges and sort pushdown
   - **Full scan** otherwise (SIMD-accelerated — often wins on wide predicates anyway)
3. The **adaptive index manager** may convert between hash and B-tree based on observed query
   shapes.

## 5.4 Index maintenance

- Indexes are kept in sync inside the same transaction as the row write.
- Add/drop indexes with `CREATE INDEX` / `DROP INDEX` SQL; `db.VacuumAsync()` reclaims space
  after large index churn.
- On bulk `InsertBatch`, indexes are updated in keyed batches to avoid re-validation per row.

> ⚡ **Guidance:** for pure point-lookup tables (by PK/unique key), a hash index + the
> `FindByPrimaryKey`/`ExecuteQueryStruct` path is the single fastest read path in the engine.
> See the [Performance Guide](performance.md).
