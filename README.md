# SharpCoreDB

<img src="https://github.com/MPCoreDeveloper/SharpCoreDB/raw/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="250">

A lightweight, encrypted, file-based database engine for .NET 10 that supports SQL operations with built-in security features. Perfect for time-tracking, invoicing, and project management applications.

## Quickstart

Install the NuGet package:

```bash
dotnet add package SharpCoreDB
```

Basic usage:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

var db = factory.Create("mydb.db", "password");
db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
var result = db.ExecuteSQL("SELECT * FROM users");
```

## Features

### Core Database Features
- **SQL Support**: Execute common SQL commands including CREATE TABLE, INSERT, SELECT, UPDATE, and DELETE
- **AES-256-GCM Encryption**: All data is encrypted at rest using industry-standard encryption
- **Write-Ahead Logging (WAL)**: Ensures durability and crash recovery
- **User Authentication**: Built-in user management with secure password hashing
- **Multiple Data Types**: Support for INTEGER, TEXT, REAL, BLOB, BOOLEAN, DATETIME, LONG, DECIMAL, ULID, and GUID
- **Auto-Generated Fields**: Automatic generation of ULID and GUID values
- **Primary Key Support**: Define primary keys for data integrity
- **JOIN Operations**: Support for INNER JOIN and LEFT JOIN queries
- **Readonly Mode**: Open databases in readonly mode for safe concurrent access
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **B-Tree Indexing**: Efficient data indexing using B-tree data structures

### New Production-Ready Features
- **Async/Await Support**: Full async support with `ExecuteSQLAsync`
- **Batch Operations**: `ExecuteBatchSQL` for bulk inserts/updates
- **Connection Pooling**: `DatabasePool`
- **Connection Strings**: `ConnectionStringBuilder`
- **Auto Maintenance**: `AutoMaintenanceService`
- **UPSERT Support**
- **Hash Index Support**: `CREATE INDEX`
- **EXPLAIN Plans**
- **Date/Time + Aggregate Functions**
- **PRAGMA Commands**
- **Modern C# 14**
- **Parameterized Queries**
- **Concurrent Async Selects**

## Performance Benchmarks (updated with latest runs)

Hardware: Windows 11 � Intel i7 � SSD � .NET 10 � DELL Precision 5550

### SELECT (parameterized and indexed)

From `QueryCache` benchmark (1000 queries on 10,000 records):

| Benchmark                  | Time (ms)    | Speedup       |
|----------------------------|--------------|---------------|
| SharpCoreDB Cached         | **320 ms**   | 1.15x faster |
| SharpCoreDB No Cache       | 369 ms       | -             |

EXPLAIN shows hash index lookup on `id` when available.

### INSERT (representative)

From `IndexOptimizationBenchmark.Insert10kRecords` and `Optimizations` (100k, pending latest run):

| Scenario                               | Records | Config            | Time (ms)   | Notes                    |
|----------------------------------------|---------|-------------------|-------------|--------------------------|
| Insert 10k (basic)                     | 10,000  | Default           | pending     | From `Insert10kRecords`  |
| Insert 10k (NoEncrypt)                 | 10,000  | HighPerformance   | pending     | No encryption speedup    |
| Insert 100k (time_entries)             | 100,000 | HighPerformance   | ~240,000    | From previous runs       |

Honest note: encrypted writes are slower than SQLite baseline (~130,000 ms for 100k). NoEncrypt narrows the gap.

### UPDATE (representative)

From tests and mixed batch operations:

| Scenario                         | Operations | Config          | Behavior            |
|----------------------------------|------------|-----------------|---------------------|
| 1k mixed INSERT/UPDATE batch     | 1,000      | Default         | < 30s (tests)       |
| UPDATE single row by PK          | 1          | Default         | Fast (B-Tree PK)    |
| UPDATE selective WHERE (indexed) | 100        | With HashIndex  | Faster (O(1) lookup)|

### NoEncryption select comparison (latest run)

From `NoEncryption` benchmark:
- 100 SELECTs: 15047 ms
- Select speedup vs encrypted: ~1.13x

### Honest comparison (basic SELECT/INSERT on 10k rows)

| Engine        | Encryption | Index | Parameterized | SELECT (ms) | INSERT (ms)      | UPDATE (pattern)         |
|---------------|------------|-------|---------------|-------------|------------------|--------------------------|
| SharpCoreDB   | AES-256    | Hash  | Yes           | 320-369     | Slower encrypted | Fast on indexed WHERE    |
| SharpCoreDB   | None       | Hash  | Yes           | 300-350     | Faster           | Fast on indexed WHERE    |
| SQLite        | None       | BTree | Yes           | ~350-450    | Faster (baseline)| Good; mature engine      |
| LiteDB        | None       | BTree | Yes           | ~400-600    | Moderate         | Moderate                 |

### Reproduce

- QueryCache: `dotnet run --project SharpCoreDB.Benchmarks/SharpCoreDB.Benchmarks.csproj -- QueryCache`
- NoEncryption: `dotnet run --project SharpCoreDB.Benchmarks/SharpCoreDB.Benchmarks.csproj -- NoEncryption`
- Optimizations (100k): `dotnet run --project SharpCoreDB.Benchmarks/SharpCoreDB.Benchmarks.csproj -- Optimizations`

## Architecture

```
+----------------+     +-----------------+     +-----------------+
|   Application  | --> |  SharpCoreDB    | --> |   File System   |
|   (C#, .NET)   |     |  SQL Engine     |     |   (.db files)   |
+----------------+     +-----------------+     +-----------------+
                        |                       |
                        |   +-----------------+ |
                        +-->|   Encryption     |<+
                            |   (AES-256-GCM)  |
                            +-----------------+
```

## Project Components

- **SharpCoreDB**
- **SharpCoreDB.EntityFrameworkCore**
- **SharpCoreDB.Extensions**
- **SharpCoreDB.Demo**
- **SharpCoreDB.Benchmarks**
- **SharpCoreDB.Tests**

## Installation

```bash
dotnet add package SharpCoreDB
```

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore
```

## Usage

### Setting Up the Database

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// Set up Dependency Injection
var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

// Get the Database Factory
var factory = serviceProvider.GetRequiredService<DatabaseFactory>();

// Create a database instance
string dbPath = "/path/to/database";
string masterPassword = "yourMasterPassword";
var db = factory.Create(dbPath, masterPassword);
```

### Creating Tables

```csharp
// Create a simple table
db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)")```

```csharp
// Create a table with various data types and primary key
db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, active BOOLEAN, created DATETIME, price DECIMAL, ulid ULID AUTO, guid GUID AUTO)");
```

### Inserting Data

```csharp
// Insert with all values
db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");

// Insert with specific columns (auto-generated fields will be filled automatically)
db.ExecuteSQL("INSERT INTO products (id, name, price) VALUES ('1', 'Widget', '19.99')");
```

### Querying Data

```csharp
// Select all records
db.ExecuteSQL("SELECT * FROM users");

// Select with WHERE clause
db.ExecuteSQL("SELECT * FROM products WHERE active = 'true'");

// Select with ORDER BY
db.ExecuteSQL("SELECT * FROM products ORDER BY name ASC");

// JOIN queries
db.ExecuteSQL("SELECT products.name, users.name FROM products JOIN users ON products.id = users.id");

// LEFT JOIN
db.ExecuteSQL("SELECT products.name, users.name FROM products LEFT JOIN users ON products.id = users.id");
```

### Updating Data

```csharp
db.ExecuteSQL("UPDATE products SET name = 'Updated Widget' WHERE id = '1'");
```

### Deleting Data

```csharp
db.ExecuteSQL("DELETE FROM products WHERE id = '1'");
```

### Readonly Mode

```csharp
// Open database in readonly mode (allows dirty reads, prevents modifications)
var dbReadonly = factory.Create(dbPath, masterPassword, isReadOnly: true);
dbReadonly.ExecuteSQL("SELECT * FROM users"); // Works
// dbReadonly.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')"); // Throws exception
```

### Async/Await Operations

```csharp
// Execute SQL asynchronously
await db.ExecuteSQLAsync("CREATE TABLE async_users (id INTEGER, name TEXT)");
await db.ExecuteSQLAsync("INSERT INTO async_users VALUES ('1', 'Alice')");

// With cancellation token
using var cts = new CancellationTokenSource();
await db.ExecuteSQLAsync("SELECT * FROM async_users", cts.Token);

// Parallel async operations
var tasks = new List<Task>();
for (int i = 0; i < 10; i++)
{
    int id = i;
    tasks.Add(db.ExecuteSQLAsync($"INSERT INTO async_users VALUES ('{id}', 'User{id}')"));
}
await Task.WhenAll(tasks);
```

### Batch Operations for High Performance

```csharp
// Create batch of SQL statements
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO users VALUES ('{i}', 'User{i}')");
}

// Execute batch in a single WAL transaction (much faster than individual inserts)
db.ExecuteBatchSQL(statements);

// Async batch operations
await db.ExecuteBatchSQLAsync(statements);

// Mixed operations in batch
var mixedBatch = new[]
{
    "INSERT INTO products VALUES ('1', 'Widget')",
    "UPDATE products SET price = '19.99' WHERE id = '1'",
    "INSERT INTO products VALUES ('2', 'Gadget')"
};
db.ExecuteBatchSQL(mixedBatch);
```

### User Management

```csharp
// Create a user
db.CreateUser("username", "password");

// Login
bool success = db.Login("username", "password");
```

### Connection Pooling

```csharp
using SharpCoreDB.Services;

// Create a database pool
var pool = new DatabasePool(services, maxPoolSize: 10);

// Get a database from the pool (reuses existing instances)
var db1 = pool.GetDatabase(dbPath, masterPassword);
var db2 = pool.GetDatabase(dbPath, masterPassword); // Same instance as db1

// Return database to pool when done
pool.ReturnDatabase(db1);

// Get pool statistics
var stats = pool.GetPoolStatistics();
Console.WriteLine($"Total: {stats["TotalConnections"]}, Active: {stats["ActiveConnections"]}");
```

### Connection Strings

```csharp
using SharpCoreDB.Services;

// Parse a connection string
var connectionString = "Data Source=app.sharpcoredb;Password=MySecret123;ReadOnly=False;Cache=Shared";
var builder = new ConnectionStringBuilder(connectionString);

// Access parsed properties
Console.WriteLine($"Database: {builder.DataSource}");
Console.WriteLine($"Read-only: {builder.ReadOnly}");

// Build a connection string
var newBuilder = new ConnectionStringBuilder
{
    DataSource = "myapp.db",
    Password = "secret",
    ReadOnly = false,
    Cache = "Private"
};
string connStr = newBuilder.BuildConnectionString();
```

### Performance Configuration

```csharp
// Default configuration with encryption enabled
var defaultConfig = DatabaseConfig.Default;
var db = factory.Create(dbPath, password, false, defaultConfig);

// Custom configuration
var config = new DatabaseConfig 
{ 
    EnableQueryCache = true,     // Enable query caching
    QueryCacheSize = 1000,       // Cache up to 1000 unique queries
    EnableHashIndexes = true,    // Enable hash indexes
    WalBufferSize = 1024 * 1024, // 1MB WAL buffer
    UseBufferedIO = false        // Standard I/O
};
var customDb = factory.Create(dbPath, password, false, config);

// High-performance mode (disables encryption - use only in trusted environments!)
var highPerfConfig = DatabaseConfig.HighPerformance;
var fastDb = factory.Create(dbPath, password, false, highPerfConfig);

// Get cache statistics
var stats = db.GetQueryCacheStatistics();
Console.WriteLine($"Hit rate: {stats.HitRate:P2}, Cached queries: {stats.Count}");
```

### Hash Indexes for Fast Queries (NEW)

SharpCoreDB now supports automatic hash indexes for 5-10x faster WHERE clause queries:

```csharp
// Create a table
db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, duration INTEGER)");

// Insert data
db.ExecuteSQL("INSERT INTO time_entries VALUES ('1', 'Alpha', '60')");
db.ExecuteSQL("INSERT INTO time_entries VALUES ('2', 'Beta', '90')");
db.ExecuteSQL("INSERT INTO time_entries VALUES ('3', 'Alpha', '30')");

// Create hash index for O(1) lookups on project column
db.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");

---

<!-- BENCHMARK_RESULTS -->
## Benchmark Results (Auto-Generated)

**Generated**: 2025-12-08 08:03:58 UTC

### Executive Summary

| Operation | Winner | Performance Advantage |
|-----------|--------|----------------------|
| SharpCoreDB.Benchmarks.Comparative.ComparativeInsertBenchmarks-20251208-085031 | **SQLite** | 2,06x faster |

### Detailed Results

#### SharpCoreDB.Benchmarks.Comparative.ComparativeInsertBenchmarks-20251208-085031

| Method | Mean | Error | StdDev | Allocated |
|--------|------|-------|--------|-----------|
| SQLite_Memory_BulkInsert | 168.688,89 ns | 6.322,72 ns | 18.968,16 ns | 0 B |
| SQLite_Memory_BulkInsert | 347.866,67 ns | 36.893,86 ns | 110.681,59 ns | 31,80 KB |
| LiteDB_BulkInsert | 624.990,00 ns | 140.020,37 ns | 442.783,29 ns | 18,08 KB |
| LiteDB_BulkInsert | 879.080,00 ns | 126.493,33 ns | 400.007,04 ns | 110,41 KB |
| SQLite_Memory_BulkInsert | 1.371.320,00 ns | 90.824,41 ns | 287.212,02 ns | 271,45 KB |
| LiteDB_BulkInsert | 3.986.460,00 ns | 239.678,51 ns | 757.930,01 ns | 1154,84 KB |
| SQLite_File_BulkInsert | 4.975.644,44 ns | 115.036,19 ns | 345.108,57 ns | 31,79 KB |
| SQLite_File_BulkInsert | 5.092.522,22 ns | 114.906,59 ns | 344.719,78 ns | 0 B |
| SharpCoreDB_Encrypted_Batch | 5.486.990,00 ns | 131.880,08 ns | 417.041,44 ns | 4128,21 KB |
| SharpCoreDB_NoEncrypt_Individual | 5.582.850,00 ns | 170.466,14 ns | 539.061,27 ns | 4130,95 KB |
| SharpCoreDB_Encrypted_Individual | 5.750.950,00 ns | 62.604,44 ns | 177.072,08 ns | 4131,97 KB |
| SQLite_File_BulkInsert | 6.072.888,89 ns | 156.075,40 ns | 468.226,21 ns | 272,43 KB |
| SharpCoreDB_NoEncrypt_Batch | 7.698.010,00 ns | 153.307,94 ns | 484.802,26 ns | 4129,30 KB |
| SQLite_Memory_BulkInsert | 9.815.762,50 ns | 624.335,96 ns | 1.765.888,77 ns | 2654,48 KB |
| SharpCoreDB_Encrypted_Batch | 13.963.544,44 ns | 184.645,84 ns | 553.937,53 ns | 4252,26 KB |
| SharpCoreDB_NoEncrypt_Batch | 16.330.450,00 ns | 266.942,76 ns | 844.147,13 ns | 4252,81 KB |
| SQLite_File_BulkInsert | 17.375.750,00 ns | 755.630,96 ns | 2.266.892,87 ns | 2670,59 KB |
| LiteDB_BulkInsert | 39.469.455,56 ns | 1.060.135,33 ns | 3.180.405,99 ns | 16621,05 KB |
| SharpCoreDB_NoEncrypt_Individual | 59.889.150,00 ns | 917.444,35 ns | 2.901.213,77 ns | 41300,12 KB |
| SharpCoreDB_Encrypted_Individual | 59.925.530,00 ns | 356.185,74 ns | 1.126.358,22 ns | 41305,49 KB |
| SharpCoreDB_NoEncrypt_Batch | 95.248.100,00 ns | 880.076,95 ns | 2.640.230,86 ns | 5493,23 KB |
| SharpCoreDB_Encrypted_Batch | 95.309.440,00 ns | 1.411.424,50 ns | 4.463.316,18 ns | 5493,90 KB |
| SharpCoreDB_NoEncrypt_Individual | 583.181.050,00 ns | 8.271.655,78 ns | 26.157.272,29 ns | 412942,00 KB |
| SharpCoreDB_Encrypted_Individual | 589.896.480,00 ns | 10.198.644,41 ns | 32.250.945,37 ns | 412987,95 KB |
| SharpCoreDB_Encrypted_Batch | 1.519.204.990,00 ns | 53.514.443,91 ns | 169.227.530,47 ns | 17903,80 KB |
| SharpCoreDB_NoEncrypt_Batch | 1.591.266.540,00 ns | 67.140.055,63 ns | 212.315.498,01 ns | 17887,47 KB |
| SharpCoreDB_NoEncrypt_Individual | 7.053.131.655,56 ns | 292.224.716,25 ns | 876.674.148,76 ns | 4128887,03 KB |
| SharpCoreDB_Encrypted_Individual | 7.081.675.260,00 ns | 214.479.155,46 ns | 678.242.641,89 ns | 4129278,66 KB |


### Performance Charts

Charts are generated in `BenchmarkDotNet.Artifacts/results/` directory.


<!-- /BENCHMARK_RESULTS -->
