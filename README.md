# SharpCoreDB

![SharpCoreDB Logo](https://github.com/MPCoreDeveloper/SharpCoreDB/raw/master/SharpCoreDB.jpg)

[![Build Status](https://github.com/MPCoreDeveloper/SharpCoreDB/workflows/CI/badge.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB/actions)
[![codecov](https://codecov.io/gh/MPCoreDeveloper/SharpCoreDB/branch/master/graph/badge.svg)](https://codecov.io/gh/MPCoreDeveloper/SharpCoreDB)

A lightweight, encrypted, file-based database engine for .NET that supports SQL operations with built-in security features. Perfect for time-tracking, invoicing, and project management applications.

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
- **Async/Await Support**: Full async support with `ExecuteSQLAsync` for non-blocking database operations
- **Batch Operations**: `ExecuteBatchSQL` for high-performance bulk inserts/updates with single WAL transaction
- **Connection Pooling**: Built-in DatabasePool class for connection reuse and resource management
- **Connection Strings**: Parse and build connection strings with ConnectionStringBuilder
- **Auto Maintenance**: Automatic VACUUM and WAL checkpointing with AutoMaintenanceService
- **UPSERT Support**: INSERT OR REPLACE and INSERT ON CONFLICT DO UPDATE syntax
- **Hash Index Support**: CREATE INDEX for O(1) WHERE clause lookups with 5-10x speedup
- **EXPLAIN Plans**: Query plan analysis with EXPLAIN command
- **Date/Time Functions**: NOW(), DATE(), STRFTIME(), DATEADD() functions
- **Aggregate Functions**: SUM(), AVG(), COUNT(DISTINCT), GROUP_CONCAT()
- **PRAGMA Commands**: table_info(), index_list(), foreign_key_list() for metadata queries
- **Modern C# 14**: Init-only properties, nullable reference types, and collection expressions

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

SharpCoreDB provides a SQL-compatible interface over encrypted file-based storage, with optional connection pooling and auto-maintenance features.

## Installation

Add the SharpCoreDB project to your solution and reference it from your application.

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

// Queries automatically use the hash index - 5-10x faster!
db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Alpha'");

// Unique indexes are also supported
db.ExecuteSQL("CREATE UNIQUE INDEX idx_user_email ON users (email)");

// Multiple indexes on different columns
db.ExecuteSQL("CREATE INDEX idx_status ON orders (status)");
db.ExecuteSQL("CREATE INDEX idx_customer ON orders (customer_id)");
```

**Performance Benefits:**
- **O(1) hash lookup** instead of O(n) table scan
- **5-10x speedup** for selective WHERE clause queries
- **Automatic maintenance** - indexes updated on INSERT/UPDATE/DELETE
- **Multiple indexes** supported per table

**Manual API (Advanced):**
```csharp
using SharpCoreDB.DataStructures;

// Create a hash index programmatically
var index = new HashIndex("time_entries", "project");
var rows = new List<Dictionary<string, object>> { /* ... */ };
index.Rebuild(rows);

// Direct lookup
var results = index.Lookup("Alpha"); // O(1) time complexity

// Get index statistics
var (uniqueKeys, totalRows, avgRowsPerKey) = index.GetStatistics();
```

### Optimized Row Parsing (NEW)

```csharp
using SharpCoreDB.Services;

// Use optimized parser with Span<byte> for minimal allocations
var jsonBytes = Encoding.UTF8.GetBytes("{\"id\":1,\"name\":\"Alice\"}");
var row = OptimizedRowParser.ParseRowOptimized(jsonBytes.AsSpan());

// Parse CSV with Span<char> for bulk imports
var csvLine = "1,Alice,30,true".AsSpan();
var columns = new List<string> { "id", "name", "age", "active" };
var parsedRow = OptimizedRowParser.ParseCsvRowOptimized(csvLine, columns);

// Build WHERE clauses with minimal allocations
var whereClause = OptimizedRowParser.BuildWhereClauseOptimized("name", "=", "Alice");
// Result: "name = 'Alice'"
```

### Auto Maintenance

```csharp
using SharpCoreDB.Services;

// Set up automatic maintenance (VACUUM and WAL checkpointing)
using var maintenance = new AutoMaintenanceService(
    db, 
    intervalSeconds: 300,      // Run every 5 minutes
    writeThreshold: 1000       // Or after 1000 writes
);

// Maintenance runs automatically in background
db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
maintenance.IncrementWriteCount(); // Track writes

// Manually trigger maintenance
maintenance.TriggerMaintenance();
```

### CREATE INDEX

```csharp
// Create an index for faster queries
db.ExecuteSQL("CREATE INDEX idx_user_email ON users (email)");

// Create a unique index
db.ExecuteSQL("CREATE UNIQUE INDEX idx_user_id ON users (id)");

````````

⚠️ **Current Implementation Notes**:
- Optimized for DummyTimeProject and time-tracking use cases
- Query execution leverages SharpCoreDB's native SQL engine
- Some advanced EF Core features (lazy loading, change tracking optimization) use default implementations
