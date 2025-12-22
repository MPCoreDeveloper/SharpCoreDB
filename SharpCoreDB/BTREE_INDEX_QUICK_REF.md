# B-Tree Index Quick Reference

## 📌 When to Use B-Tree vs Hash Indexes

```
Query Type                    | Index Type | Performance
------------------------------|------------|-------------
WHERE age = 30                | Hash       | 0.5ms  (O(1))
WHERE age > 30                | B-tree     | 8ms    (O(log n + k))
WHERE age BETWEEN 25 AND 35   | B-tree     | 12ms   (O(log n + k))
ORDER BY age                  | B-tree     | 4ms    (sorted iteration)
WHERE name = 'Alice'          | Hash       | 0.5ms  (O(1))
WHERE name LIKE 'A%'          | None       | 30ms   (full scan)
```

## 🔧 SQL Syntax

```sql
-- Create B-tree index
CREATE INDEX idx_age ON users (age) USING BTREE;

-- Create hash index (default)
CREATE INDEX idx_email ON users (email);

-- Drop any index
DROP INDEX idx_age;
```

## 💻 C# Usage

```csharp
using SharpCoreDB;

var db = factory.Create("./mydb", "password");

// Setup
db.ExecuteSQL("CREATE TABLE users (id INTEGER, age INTEGER, name TEXT)");
db.ExecuteSQL("CREATE INDEX idx_age ON users (age) USING BTREE");

// Range query (uses B-tree)
var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 40");
// Expected: 8ms (vs 30ms without index)

// Sorted query (uses B-tree)
var sorted = db.ExecuteQuery("SELECT * FROM users ORDER BY age");
// Expected: 4ms (vs 40ms without index)
```

## 📊 Performance Targets (10k records)

| Operation | Without Index | With B-Tree | Speedup |
|-----------|---------------|-------------|---------|
| Range query | 30ms | 8ms | **3.5x** |
| BETWEEN | 30ms | 12ms | **2.5x** |
| ORDER BY | 40ms | 4ms | **9x** |
| Point lookup | 30ms | 2ms | **15x** |

## 🎯 Best Practices

```sql
-- ✅ DO: Use hash for exact matches
CREATE INDEX idx_email ON users (email);

-- ✅ DO: Use B-tree for ranges and sorting
CREATE INDEX idx_age ON users (age) USING BTREE;
CREATE INDEX idx_salary ON employees (salary) USING BTREE;

-- ✅ DO: Use B-tree for DateTime ranges
CREATE INDEX idx_order_date ON orders (order_date) USING BTREE;

-- ❌ DON'T: Use B-tree for exact matches (hash is faster)
-- CREATE INDEX idx_username ON users (username) USING BTREE;  -- Use HASH instead!

-- ❌ DON'T: Index columns not used in WHERE/ORDER BY
-- CREATE INDEX idx_unused ON users (middle_name) USING BTREE;  -- Wasted memory!
```

## 🔍 Supported Types

| Type | Hash Index | B-Tree Index | Use Case |
|------|------------|--------------|----------|
| INTEGER | ✅ | ✅ | IDs, counts, age |
| LONG | ✅ | ✅ | Large numbers |
| DECIMAL | ✅ | ✅ | **Salary, prices** |
| REAL | ✅ | ✅ | Measurements |
| TEXT | ✅ | ✅ | Names, emails |
| DATETIME | ✅ | ✅ | **Dates, timestamps** |
| BOOLEAN | ✅ | ❌ | Flags (use hash) |
| BLOB | ❌ | ❌ | Not indexable |

## 🧪 Testing

```bash
# Run benchmarks
cd SharpCoreDB.Benchmarks
dotnet run -c Release --filter "*BTreeIndexRangeQueryBenchmark*"

# Run unit tests
cd SharpCoreDB.Tests
dotnet test --filter "FullyQualifiedName~BTreeIndexTests"
```

## 📚 More Information

- **Full Guide:** [BTREE_INDEX_GUIDE.md](BTREE_INDEX_GUIDE.md)
- **Implementation:** [BTREE_INDEX_IMPLEMENTATION_SUMMARY.md](BTREE_INDEX_IMPLEMENTATION_SUMMARY.md)
- **General Usage:** [USAGE.md](USAGE.md)
