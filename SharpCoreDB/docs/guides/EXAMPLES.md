# SharpCoreDB Examples

**Last Updated**: 2025-12-13

## Table of Contents

1. [Basic CRUD Operations](#basic-crud)
2. [Transactions](#transactions)
3. [Indexes](#indexes)
4. [MVCC & Concurrency](#mvcc)
5. [Caching](#caching)
6. [Adaptive WAL Batching](#adaptive-wal-batching) - ⚡ **NEW!**
7. [Entity Framework Core](#ef-core)
8. [Advanced Queries](#advanced-queries)
9. [Read-only Instances & Schema Changes](#read-only-instances--schema-changes)  <!-- NEW -->

---

## Basic CRUD

### Create Table

```csharp
var db = new SharpCoreDatabase();
db.Execute("CREATE TABLE users (id INT, name TEXT, email TEXT)");
```

### Insert Data

```csharp
db.Execute("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com')");
db.Execute("INSERT INTO users VALUES (2, 'Bob', 'bob@example.com')");
```

### Query Data

```csharp
var results = db.Query("SELECT * FROM users WHERE id = 1");
foreach (var row in results)
{
    Console.WriteLine($"Name: {row["name"]}, Email: {row["email"]}");
}
```

### Update Data

```csharp
db.Execute("UPDATE users SET email = 'alice.new@example.com' WHERE id = 1");
```

### Delete Data

```csharp
db.Execute("DELETE FROM users WHERE id = 2");
```

---

## Transactions

### Basic Transaction

```csharp
using var tx = db.BeginTransaction();

try
{
    tx.Execute("UPDATE accounts SET balance = balance - 100 WHERE id = 1");
    tx.Execute("UPDATE accounts SET balance = balance + 100 WHERE id = 2");
    tx.Commit();
}
catch (Exception ex)
{
    tx.Rollback();
    Console.WriteLine($"Transaction failed: {ex.Message}");
}
```

### Snapshot Isolation

```csharp
var tx1 = db.BeginTransaction(IsolationLevel.Snapshot);
var tx2 = db.BeginTransaction(IsolationLevel.Snapshot);

// tx1 reads data
var balance1 = tx1.Query("SELECT balance FROM accounts WHERE id = 1");

// tx2 modifies data
tx2.Execute("UPDATE accounts SET balance = 1000 WHERE id = 1");
tx2.Commit();

// tx1 still sees original snapshot
var balance2 = tx1.Query("SELECT balance FROM accounts WHERE id = 1");
// balance1 == balance2 (snapshot isolation!)

tx1.Commit();
```

---

## Indexes

### Create Index

```csharp
db.Execute("CREATE INDEX idx_email ON users(email)");
```

### B-Tree Index (Default)

```csharp
db.Execute("CREATE INDEX idx_id ON users(id)");
// Automatically uses B-Tree for range queries
```

### Hash Index

```csharp
db.Execute("CREATE HASH INDEX idx_email_hash ON users(email)");
// Faster for equality lookups
```

### Query with Index

```csharp
// Uses idx_email automatically
var users = db.Query("SELECT * FROM users WHERE email = 'alice@example.com'");
```

### Check Index Usage

```csharp
var plan = db.Query("EXPLAIN SELECT * FROM users WHERE email = 'alice@example.com'");
// Shows: "Index Scan using idx_email"
```

---

## MVCC & Concurrency

### Concurrent Reads (No Blocking)

```csharp
var tasks = new List<Task>();

for (int i = 0; i < 10; i++)
{
    tasks.Add(Task.Run(() =>
    {
        var results = db.Query("SELECT * FROM users");
        // All reads execute concurrently without locks
    }));
}

await Task.WhenAll(tasks);
```

### Write Conflicts

```csharp
var tx1 = db.BeginTransaction();
var tx2 = db.BeginTransaction();

tx1.Execute("UPDATE users SET name = 'Alice Updated' WHERE id = 1");

// tx2 tries to update same row
try
{
    tx2.Execute("UPDATE users SET name = 'Alice Conflict' WHERE id = 1");
    tx2.Commit(); // Throws WriteConflictException
}
catch (WriteConflictException)
{
    tx2.Rollback();
    Console.WriteLine("Write conflict detected!");
}

tx1.Commit();
```

---

## Caching

### Enable Query Cache

```csharp
var options = new DatabaseOptions
{
    EnableQueryCache = true,
    QueryCacheSize = 10000
};

var db = new SharpCoreDatabase(options);
```

### Cache Hit Example

```csharp
// First query: cache miss (0.08 ms)
var result1 = db.Query("SELECT * FROM users WHERE id = 1");

// Second query: cache hit (0.04 ms - 50% faster!)
var result2 = db.Query("SELECT * FROM users WHERE id = 1");
```

### Configure Cache at Runtime

```csharp
db.Execute("PRAGMA query_cache_size = 20000");
```

### Clear Cache

```csharp
db.Execute("PRAGMA query_cache_clear");
```

---

## Adaptive WAL Batching

> ⚡ **NEW!** Improve write performance with adaptive WAL batching.

### Enable Adaptive Batching (Recommended)

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

// Option 1: Use HighPerformance preset (adaptive enabled by default)
var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

var db = factory.Create(
    dbPath: "./data",
    password: "mypassword",
    config: DatabaseConfig.HighPerformance  // ✅ Adaptive enabled
);

// Batch size now adapts automatically based on load!
// Low load (2 threads):  512 operations
// Medium load (8 threads): 2048 operations  
// High load (32+ threads): 8192 operations
```

### Configure for High Concurrency

```csharp
// For highly concurrent workloads (32+ threads)
var config = DatabaseConfig.Concurrent;  // Aggressive adaptive batching

var db = factory.Create("./data", "mypassword", config: config);

// Simulate high concurrency
Parallel.For(0, 64, i =>
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ({i}, 'Event {i}', '{DateTime.Now:o}')");
});

// Console output shows batch size scaling:
// [GroupCommitWAL:a1b2c3d4] Batch size adjusted: 2048 → 4096 (queue depth: 10000)
// [GroupCommitWAL:a1b2c3d4] Batch size adjusted: 4096 → 8192 (queue depth: 20000)
```

### Custom Multiplier for Extreme Load

```csharp
// For extreme concurrency (64+ threads):
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,
    WalBatchMultiplier = 256,  // More aggressive (ProcessorCount * 256)
    WalDurabilityMode = DurabilityMode.Async,
    NoEncryptMode = true,
};

var db = factory.Create("./data", "mypassword", config: config);

// Initial batch size on 8-core system: 8 * 256 = 2048
// Can scale up to 10,000 operations
```

### Monitor Batch Size Adjustments

```csharp
// Get current batch size (useful for monitoring dashboards)
var wal = db.GetGroupCommitWAL();  // Internal API

int currentBatchSize = wal.GetCurrentBatchSize();
var (current, adjustments, enabled) = wal.GetAdaptiveBatchStatistics();

Console.WriteLine($"Current batch size: {current}");
Console.WriteLine($"Total adjustments: {adjustments}");
Console.WriteLine($"Adaptive batching: {(enabled ? "Enabled" : "Disabled")}");

// Example output:
// Current batch size: 4096
// Total adjustments: 5
// Adaptive batching: Enabled
```

### Disable Adaptive (Fixed Batch Size)

```csharp
// For fixed, predictable workloads:
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = false,  // Disable adaptive
    WalMaxBatchSize = 5000,              // Fixed batch size
};

var db = factory.Create("./data", "mypassword", config: config);

// Batch size stays constant at 5000 regardless of load
```

### Performance Comparison

```csharp
using System.Diagnostics;

// Test with 32 concurrent threads
var stopwatch = Stopwatch.StartNew();

Parallel.For(0, 32, threadId =>
{
    for (int i = 0; i < 1000; i++)
    {
        int recordId = threadId * 1000 + i;
        db.ExecuteSQL($"INSERT INTO test VALUES ({recordId}, 'Data {recordId}')");
    }
});

stopwatch.Stop();
Console.WriteLine($"Inserted 32,000 records in {stopwatch.ElapsedMilliseconds} ms");

// Expected results (8-core system):
// Fixed batching (1000):   ~850ms
// Adaptive batching:       ~680ms  (+20% faster at 32 threads!)
```

### Load-Based Scaling Example

```csharp
// Simulate variable load
var db = factory.Create("./data", "password", config: DatabaseConfig.HighPerformance);

// Phase 1: Low load (2 threads)
Parallel.For(0, 2, i =>
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ({i}, 'Low load')");
});
// Batch size scales DOWN: 1024 → 512

await Task.Delay(1000);

// Phase 2: Medium load (8 threads)
Parallel.For(0, 8, i =>
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ({i}, 'Medium load')");
});
// Batch size stays: 1024

await Task.Delay(1000);

// Phase 3: High load (32 threads)
Parallel.For(0, 32, i =>
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ({i}, 'High load')");
});
// Batch size scales UP: 1024 → 2048 → 4096

// Console shows adjustments:
// [GroupCommitWAL:a1b2c3d4] Batch size adjusted: 1024 → 512 (queue depth: 120)
// [GroupCommitWAL:a1b2c3d4] Batch size adjusted: 512 → 1024 (queue depth: 800)
// [GroupCommitWAL:a1b2c3d4] Batch size adjusted: 1024 → 2048 (queue depth: 5000)
```

### When NOT to Use Adaptive Batching

```csharp
// Single-threaded benchmarks: Use Benchmark config (adaptive disabled)
var benchmarkDb = factory.Create(
    "./benchmark",
    "password",
    config: DatabaseConfig.Benchmark  // Optimized for single-thread
);

// Adaptive batching is disabled for predictable benchmark results
```

---

## Entity Framework Core

### Define Entity

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
```

### DbContext

```csharp
public class ShopContext : DbContext
{
    public DbSet<Product> Products { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSharpCoreDB("Data Source=:memory:");
    }
}
```

### LINQ Queries

```csharp
using var context = new ShopContext();
context.Database.EnsureCreated();

// Add products
context.Products.Add(new Product { Id = 1, Name = "Laptop", Price = 999.99m });
context.SaveChanges();

// Query with LINQ
var expensiveProducts = context.Products
    .Where(p => p.Price > 500)
    .OrderBy(p => p.Name)
    .ToList();
```

---

## Advanced Queries

### JOIN

```csharp
var results = db.Query(@"
    SELECT u.name, o.total
    FROM users u
    INNER JOIN orders o ON u.id = o.user_id
    WHERE o.total > 100
");
```

### Aggregates

```csharp
var stats = db.Query(@"
    SELECT 
        COUNT(*) as total,
        AVG(price) as avg_price,
        MAX(price) as max_price,
        MIN(price) as min_price
    FROM products
");
```

### GROUP BY

```csharp
var summary = db.Query(@"
    SELECT category, COUNT(*) as count
    FROM products
    GROUP BY category
    HAVING count > 5
");
```

### Subqueries

```csharp
var results = db.Query(@"
    SELECT * FROM products
    WHERE price > (SELECT AVG(price) FROM products)
");
```

---

## Read-only Instances & Schema Changes

Read-only instances load the catalog (tables/indexes) once on construction. If a read-write instance creates a new table later, already opened read-only instances do not automatically “see” the new table.

- Effect: queries against newly created tables fail on those read-only instances until they are recreated.
- Cost: recreating a read-only instance only reloads small metadata files (O(#tables)) and has negligible overhead. It does not affect regular query performance.

### Recommended patterns

```csharp
// 1) Create schema with a read-write connection
var rw = factory.Create(dbPath, password);
rw.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
