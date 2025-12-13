# Benchmark Implementation Guide

## Overview

This guide explains how to create fair, comprehensive benchmarks comparing SharpCoreDB with SQLite and LiteDB.

---

## 🎯 Benchmark Structure

### Required Comparisons

**Databases to Compare:**
1. **SQLite** (Microsoft.Data.Sqlite) - Industry standard
2. **LiteDB** - Popular .NET embedded DB
3. **SharpCoreDB** (No Encryption) - Best-case performance
4. **SharpCoreDB** (Encrypted) - Real-world usage

### Operations to Test

**CRUD Operations (10,000 records):**
- Sequential INSERT
- Batch INSERT (transaction/bulk)
- SELECT by ID (1,000 queries)
- UPDATE (1,000 records)
- DELETE (1,000 records)

**Analytics (100,000 records):**
- SUM aggregate
- AVG aggregate
- MIN aggregate
- MAX aggregate
- COUNT aggregate
- Full table scan
- Filtered scan (WHERE clause)

---

## 📝 Implementation Template

### 1. Create Benchmark Class

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using LiteDB;
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Benchmarks.Comparison;

/// <summary>
/// Fair comparison benchmark for [OPERATION]
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class [Operation]ComparisonBenchmark
{
    private const int RecordCount = 10_000;
    
    // Database instances
    private SqliteConnection _sqliteConn = null!;
    private LiteDatabase _liteDb = null!;
    private IDatabase _sharpCoreDb = null!;
    private IDatabase _sharpCoreDbEncrypted = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        // Initialize all databases with same data
    }
    
    [GlobalCleanup]
    public void Cleanup()
    {
        // Dispose connections and cleanup files
    }
}
```

### 2. Implement SQLite Benchmark

```csharp
[Benchmark(Description = "SQLite - [OPERATION]")]
public void SQLite_Operation()
{
    // Setup connection
    _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
    _sqliteConn.Open();
    
    // Create table
    using var cmd = _sqliteConn.CreateCommand();
    cmd.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER)";
    cmd.ExecuteNonQuery();
    
    // Enable WAL mode for fair comparison
    cmd.CommandText = "PRAGMA journal_mode=WAL";
    cmd.ExecuteNonQuery();
    
    // Perform operation
    // For batch operations, use transaction:
    using var transaction = _sqliteConn.BeginTransaction();
    cmd.Transaction = transaction;
    
    cmd.CommandText = "INSERT INTO users (name, email, age) VALUES (@name, @email, @age)";
    cmd.Parameters.Add("@name", SqliteType.Text);
    cmd.Parameters.Add("@email", SqliteType.Text);
    cmd.Parameters.Add("@age", SqliteType.Integer);
    
    for (int i = 0; i < RecordCount; i++)
    {
        cmd.Parameters["@name"].Value = $"User{i}";
        cmd.Parameters["@email"].Value = $"user{i}@example.com";
        cmd.Parameters["@age"].Value = 20 + (i % 50);
        cmd.ExecuteNonQuery();
    }
    
    transaction.Commit();
    _sqliteConn.Close();
}
```

### 3. Implement LiteDB Benchmark

```csharp
[Benchmark(Description = "LiteDB - [OPERATION]")]
public void LiteDB_Operation()
{
    _liteDb = new LiteDatabase(_litedbPath);
    var col = _liteDb.GetCollection<User>("users");
    
    // For bulk operations
    var users = Enumerable.Range(0, RecordCount).Select(i => new User
    {
        Name = $"User{i}",
        Email = $"user{i}@example.com",
        Age = 20 + (i % 50)
    });
    
    col.InsertBulk(users);
    _liteDb.Dispose();
}

private class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
}
```

### 4. Implement SharpCoreDB Benchmark

```csharp
[Benchmark(Description = "SharpCoreDB - [OPERATION] (No Encryption)")]
public void SharpCoreDB_Operation()
{
    var services = new ServiceCollection();
    services.AddSharpCoreDB();
    var provider = services.BuildServiceProvider();
    var factory = provider.GetRequiredService<DatabaseFactory>();
    
    _sharpCoreDb = factory.Create(_sharpCoreDbPath, "password", 
        config: new DatabaseConfig { 
            NoEncryptMode = true,
            UseGroupCommitWal = true  // Fair comparison with SQLite WAL
        });
    
    _sharpCoreDb.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");
    
    // For bulk operations
    var batch = Enumerable.Range(0, RecordCount).Select(i =>
        $"INSERT INTO users VALUES ({i}, 'User{i}', 'user{i}@example.com', {20 + (i % 50)})").ToList();
    
    _sharpCoreDb.ExecuteBatchSQL(batch);
    _sharpCoreDb.Dispose();
}

[Benchmark(Description = "SharpCoreDB - [OPERATION] (Encrypted)")]
public void SharpCoreDB_Operation_Encrypted()
{
    var services = new ServiceCollection();
    services.AddSharpCoreDB();
    var provider = services.BuildServiceProvider();
    var factory = provider.GetRequiredService<DatabaseFactory>();
    
    _sharpCoreDbEncrypted = factory.Create(_sharpCoreDbEncryptedPath, "password",
        config: new DatabaseConfig { UseGroupCommitWal = true });
    
    _sharpCoreDbEncrypted.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");
    
    var batch = Enumerable.Range(0, RecordCount).Select(i =>
        $"INSERT INTO users VALUES ({i}, 'User{i}', 'user{i}@example.com', {20 + (i % 50)})").ToList();
    
    _sharpCoreDbEncrypted.ExecuteBatchSQL(batch);
    _sharpCoreDbEncrypted.Dispose();
}
```

---

## 🎯 Fair Comparison Guidelines

### 1. Use Same Data

```csharp
// All databases should insert identical data
for (int i = 0; i < RecordCount; i++)
{
    var id = i;
    var name = $"User{i}";
    var email = $"user{i}@example.com";
    var age = 20 + (i % 50);
    
    // Insert into all databases
}
```

### 2. Enable Equivalent Features

| Feature | SQLite | LiteDB | SharpCoreDB |
|---------|--------|--------|-------------|
| WAL Mode | `PRAGMA journal_mode=WAL` | N/A (auto) | `UseGroupCommitWal = true` |
| Transaction | `BeginTransaction()` | `InsertBulk()` | `ExecuteBatchSQL()` |
| Index | `CREATE INDEX` | `EnsureIndex()` | `CreateHashIndex()` |

### 3. Warm Up Caches

```csharp
[GlobalSetup]
public void Setup()
{
    // Run each operation once to warm up
    SQLite_Operation();
    LiteDB_Operation();
    SharpCoreDB_Operation();
    
    // Then cleanup for actual benchmark
    Cleanup();
}
```

### 4. Clean Between Runs

```csharp
[IterationSetup]
public void IterationSetup()
{
    // Delete database files before each iteration
    CleanupDatabases();
}

private void CleanupDatabases()
{
    try
    {
        if (File.Exists(_sqlitePath)) File.Delete(_sqlitePath);
        if (File.Exists(_litedbPath)) File.Delete(_litedbPath);
        if (Directory.Exists(_sharpCoreDbPath)) 
            Directory.Delete(_sharpCoreDbPath, true);
    }
    catch { /* Ignore */ }
}
```

---

## 📊 Expected Results Template

Document expected results honestly:

```markdown
### [OPERATION] Benchmark Results

| Database | Configuration | Time (ms) | Ops/sec | Rank |
|----------|--------------|-----------|---------|------|
| [Winner] | ... | X ms 🥇 | Y ops/sec | 1st |
| [Second] | ... | X ms 🥈 | Y ops/sec | 2nd |
| [Third] | ... | X ms 🥉 | Y ops/sec | 3rd |

**Analysis:**
- ✅ [Winner] wins because [reason]
- ⚠️ SharpCoreDB is Xx slower because [honest reason]
- 💡 Recommendation: [when to use each DB]

**Why [Winner] wins:**
1. [Technical reason 1]
2. [Technical reason 2]
```

---

## 🚨 Common Pitfalls

### ❌ DON'T: Cherry-pick results

```csharp
// BAD: Only showing operations where SharpCoreDB wins
[Benchmark] public void OnlyFastOperation() { }
```

### ✅ DO: Show all results

```csharp
// GOOD: Show all operations, even where SharpCoreDB loses
[Benchmark] public void SequentialInsert() { }  // SQLite wins
[Benchmark] public void IndexedLookup() { }     // SharpCoreDB wins
[Benchmark] public void AggregateSUM() { }      // SharpCoreDB wins
```

### ❌ DON'T: Use unfair configurations

```csharp
// BAD: SharpCoreDB with indexes, SQLite without
table.CreateHashIndex("id");  // SharpCoreDB only
```

### ✅ DO: Enable indexes for all

```csharp
// GOOD: All databases get indexes
// SQLite
cmd.CommandText = "CREATE INDEX idx_id ON users(id)";

// LiteDB
col.EnsureIndex(x => x.Id);

// SharpCoreDB
table.CreateHashIndex("id");
```

---

## 📈 Running Benchmarks

```bash
# Run all comparison benchmarks
dotnet run --project SharpCoreDB.Benchmarks -c Release

# Run specific category
dotnet run -c Release -- --filter *Insert*
dotnet run -c Release -- --filter *Select*
dotnet run -c Release -- --filter *Aggregate*

# Generate HTML report
dotnet run -c Release -- --exporters html

# Short job for quick testing
dotnet run -c Release -- --job short
```

---

## 📚 Documentation Template

For each benchmark category, create documentation:

```markdown
# [Operation] Benchmark Results

## Setup
- Records: [count]
- Hardware: [specs]
- Software: SQLite [version], LiteDB [version], SharpCoreDB [version]

## Results
[Tables and charts]

## Analysis
### SQLite Wins Because:
- [Reason 1]
- [Reason 2]

### SharpCoreDB Position:
- [Where it's competitive]
- [Where it falls short]
- [Recommendations]

## Use Cases
- Use SQLite when: [scenarios]
- Use SharpCoreDB when: [scenarios]
```

---

## ✅ Benchmark Checklist

Before publishing benchmarks:

- [ ] All databases use identical data
- [ ] Equivalent features enabled (WAL, transactions, indexes)
- [ ] Warm-up runs performed
- [ ] Clean between iterations
- [ ] Release mode build
- [ ] Memory diagnostics enabled
- [ ] Results documented honestly
- [ ] Weaknesses acknowledged
- [ ] Recommendations provided
- [ ] Methodology explained

---

## 🔗 Full Example

See complete implementation in:
- `docs/benchmarks/DATABASE_COMPARISON.md` - Full results and analysis
- This template for creating new benchmarks

---

*Benchmark guide maintained by MPCoreDeveloper*  
*Last updated: January 2025*
