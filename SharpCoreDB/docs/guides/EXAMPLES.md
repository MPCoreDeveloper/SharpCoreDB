# SharpCoreDB Examples

**Last Updated**: 2025-12-13

## Table of Contents

1. [Basic CRUD Operations](#basic-crud)
2. [Transactions](#transactions)
3. [Indexes](#indexes)
4. [MVCC & Concurrency](#mvcc)
5. [Caching](#caching)
6. [Entity Framework Core](#ef-core)
7. [Advanced Queries](#advanced-queries)

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

## Performance Tips

1. **Use indexes** for frequently queried columns
2. **Enable caching** for read-heavy workloads
3. **Batch inserts** instead of individual inserts
4. **Use transactions** for multiple operations
5. **Choose correct index type** (B-Tree for ranges, Hash for equality)

---

For more examples, see:
- [Benchmarks](../SharpCoreDB.Benchmarks/)
- [Demo Project](../SharpCoreDB.Demo/)
- [Unit Tests](../SharpCoreDB.Tests/)
