# B-Tree Indexes in SharpCoreDB

## Overview

SharpCoreDB now supports **B-tree indexes** for efficient range queries, in addition to existing hash indexes for equality lookups. B-tree indexes enable:

- **Range queries** (`WHERE age > 30`, `WHERE salary BETWEEN 50000 AND 100000`)
- **Sorted iteration** (`ORDER BY created_at`)
- **Min/Max queries** (O(log n) instead of O(n))

## Performance Comparison

| Operation | Full Scan | Hash Index | B-Tree Index |
|-----------|-----------|------------|--------------|
| Point lookup (`WHERE age = 30`) | ~30ms | **< 0.5ms** | ~2ms |
| Range query (`WHERE age > 30`) | ~30ms | ~30ms (falls back to scan) | **< 10ms** |
| ORDER BY | ~40ms (scan + sort) | ~40ms | **< 5ms** (sorted) |
| BETWEEN query | ~30ms | ~30ms | **< 15ms** |

**Key Takeaways:**
- Use **hash indexes** for equality lookups (fastest)
- Use **B-tree indexes** for range queries and sorting
- Both can coexist on different columns

## SQL Syntax

### Create B-Tree Index

```sql
-- Basic syntax
CREATE INDEX idx_age ON users (age) USING BTREE;

-- Also supported (shorthand)
CREATE INDEX idx_age BTREE ON users (age);

-- Named index
CREATE INDEX idx_salary_range ON employees (salary) USING BTREE;
```

### Create Hash Index (for comparison)

```sql
-- Hash index (default, backward compatible)
CREATE INDEX idx_email ON users (email);

-- Explicit hash syntax
CREATE INDEX idx_email ON users (email) USING HASH;
```

### Drop Index

```sql
-- Works for both hash and B-tree indexes
DROP INDEX idx_age;
DROP INDEX IF EXISTS idx_salary_range;
```

## Usage Examples

### Example 1: Range Query on Numeric Column

```csharp
using SharpCoreDB;

var db = factory.Create("./mydb", "password");

// Create table
db.ExecuteSQL(@"
    CREATE TABLE employees (
        id INTEGER PRIMARY KEY,
        name TEXT,
        age INTEGER,
        salary DECIMAL
    )
");

// Create B-tree index on salary for range queries
db.ExecuteSQL("CREATE INDEX idx_salary ON employees (salary) USING BTREE");

// Insert test data
for (int i = 1; i <= 10000; i++)
{
    db.ExecuteSQL("INSERT INTO employees VALUES (@0, @1, @2, @3)",
        new Dictionary<string, object?> {
            { "0", i },
            { "1", $"Employee{i}" },
            { "2", 20 + (i % 45) },
            { "3", 30000 + (i * 5) }
        });
}

// Query 1: Range query (uses B-tree index)
// Expected: < 10ms with B-tree vs ~30ms full scan
var highEarners = db.ExecuteQuery(
    "SELECT * FROM employees WHERE salary > 80000"
);
Console.WriteLine($"High earners: {highEarners.Count}");

// Query 2: BETWEEN query (uses B-tree index)
var midRange = db.ExecuteQuery(
    "SELECT * FROM employees WHERE salary >= 50000 AND salary <= 70000"
);
Console.WriteLine($"Mid-range salaries: {midRange.Count}");
```

### Example 2: Sorted Results with ORDER BY

```csharp
// Create B-tree index on age for sorted queries
db.ExecuteSQL("CREATE INDEX idx_age ON employees (age) USING BTREE");

// Query with ORDER BY (uses B-tree sorted iteration)
// Expected: < 5ms with B-tree vs ~40ms (scan + external sort)
var sortedByAge = db.ExecuteQuery(
    "SELECT * FROM employees ORDER BY age"
);

// Oldest employees
var oldest = sortedByAge.TakeLast(10);
foreach (var employee in oldest)
{
    Console.WriteLine($"{employee["name"]}: {employee["age"]} years old");
}
```

### Example 3: DateTime Range Queries

```csharp
db.ExecuteSQL(@"
    CREATE TABLE orders (
        order_id INTEGER PRIMARY KEY,
        customer_name TEXT,
        order_date DATETIME,
        total_amount DECIMAL
    )
");

// Create B-tree index on order_date
db.ExecuteSQL("CREATE INDEX idx_order_date ON orders (order_date) USING BTREE");

// Query orders in date range (uses B-tree index)
var recentOrders = db.ExecuteQuery(@"
    SELECT * FROM orders 
    WHERE order_date >= '2024-01-01' AND order_date <= '2024-12-31'
");
```

### Example 4: Combining Hash and B-Tree Indexes

```csharp
db.ExecuteSQL(@"
    CREATE TABLE products (
        product_id INTEGER PRIMARY KEY,
        category TEXT,
        name TEXT,
        price DECIMAL,
        stock INTEGER
    )
");

// Hash index for exact category lookups (O(1))
db.ExecuteSQL("CREATE INDEX idx_category ON products (category)");

// B-tree index for price range queries (O(log n + k))
db.ExecuteSQL("CREATE INDEX idx_price ON products (price) USING BTREE");

// Query 1: Exact match uses hash index (fastest)
var electronics = db.ExecuteQuery(
    "SELECT * FROM products WHERE category = 'Electronics'"
);

// Query 2: Range query uses B-tree index
var affordableProducts = db.ExecuteQuery(
    "SELECT * FROM products WHERE price < 100.00"
);

// Query 3: Combined (hash filter + B-tree range)
var affordableElectronics = db.ExecuteQuery(@"
    SELECT * FROM products 
    WHERE category = 'Electronics' AND price < 100.00
");
```

## Index Selection Strategy

SharpCoreDB automatically chooses the best index type for your query:

| Query Pattern | Index Used | Reason |
|---------------|------------|--------|
| `WHERE age = 30` | Hash (if exists) | O(1) point lookup |
| `WHERE age > 30` | B-tree (if exists) | Range scan |
| `WHERE age BETWEEN 25 AND 35` | B-tree (if exists) | Range scan |
| `ORDER BY age` | B-tree (if exists) | Sorted iteration |
| `WHERE name = 'Alice'` | Hash (if exists) | Exact match |
| `WHERE name LIKE 'A%'` | Full scan | No index helps |

**Best Practice:** Create both types on different columns based on query patterns:

```sql
-- Columns queried with = → Hash index
CREATE INDEX idx_email ON users (email);
CREATE INDEX idx_status ON orders (status);

-- Columns queried with >, <, BETWEEN, ORDER BY → B-tree index
CREATE INDEX idx_age ON users (age) USING BTREE;
CREATE INDEX idx_price ON products (price) USING BTREE;
CREATE INDEX idx_order_date ON orders (order_date) USING BTREE;
```

## Technical Details

### B-Tree Structure

- **Degree**: 3 (configurable in `BTree.cs`)
- **Node capacity**: 6 keys (2 × degree)
- **Height**: O(log n) for n keys
- **Lookup complexity**: O(log n)
- **Range scan complexity**: O(log n + k) where k = result count

### Memory Overhead

| Index Type | Bytes per Entry | Best For |
|------------|-----------------|----------|
| Hash | ~24 bytes | Exact matches |
| B-tree | ~40-60 bytes | Ranges, sorting |

For 10,000 records:
- Hash index: ~240 KB
- B-tree index: ~400-600 KB

### Supported Data Types

B-tree indexes support all comparable types:

- ✅ **INTEGER** (int, long)
- ✅ **REAL** (double)
- ✅ **DECIMAL** (decimal)
- ✅ **TEXT** (string) - lexicographic ordering
- ✅ **DATETIME** (DateTime) - chronological ordering
- ❌ **BOOLEAN** - Use hash index instead
- ❌ **BLOB** - Not comparable

## Benchmarks

Run the included benchmark to see performance improvements:

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release --filter "*BTreeIndexRangeQueryBenchmark*"
```

Expected results (10k records):

```
| Method                    | Mean      | Ratio |
|-------------------------- |----------:|------:|
| BTreeIndex_OrderBy        |   4.2 ms  |  0.11 |
| BTreeIndex_RangeQuery     |   8.5 ms  |  0.28 |
| BTreeIndex_BetweenQuery   |  12.3 ms  |  0.41 |
| FullTableScan_RangeQuery  |  30.1 ms  |  1.00 |
| FullTableScan_OrderBy     |  39.8 ms  |  1.32 |
```

**Improvement: 3-9x faster for range queries! 🚀**

## Limitations

1. **Write overhead**: B-tree inserts are O(log n) vs O(1) for hash
   - For write-heavy workloads, consider deferring index updates with batch operations
   
2. **Memory usage**: B-trees use ~2x memory of hash indexes
   - Monitor memory if indexing millions of records
   
3. **Prefix matching**: `LIKE 'prefix%'` not yet optimized with B-tree
   - Use full-text search or custom indexing for this use case

## Migration Guide

Existing databases work without changes! To add B-tree indexes:

```sql
-- Check existing indexes
PRAGMA index_list(users);

-- Add B-tree index for range queries
CREATE INDEX idx_age_btree ON users (age) USING BTREE;

-- Keep existing hash indexes for exact matches
-- (they coexist peacefully)
```

## Roadmap

Future enhancements planned:

- [ ] Composite B-tree indexes (`CREATE INDEX ON users (age, salary)`)
- [ ] Covering indexes (include non-key columns)
- [ ] Online index rebuilding (no table lock)
- [ ] Index compression for better memory efficiency
- [ ] Query planner hints (`USE INDEX (idx_name)`)

## References

- [USAGE.md](USAGE.md) - General usage guide
- [OPTIMIZATION_ROADMAP.md](docs/optimization/OPTIMIZATION_ROADMAP.md) - Performance optimization roadmap
- [BTree.cs](SharpCoreDB/DataStructures/BTree.cs) - B-tree implementation
- [BTreeIndex.cs](SharpCoreDB/DataStructures/BTreeIndex.cs) - B-tree index wrapper

---

**Questions or issues?** Open an issue on [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
