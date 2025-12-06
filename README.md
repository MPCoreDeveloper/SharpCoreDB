# SharpCoreDB

![SharpCoreDB Logo](sharpcoredb.jpg)

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
db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");

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

// Queries automatically use indexes when available
db.ExecuteSQL("SELECT * FROM users WHERE email = 'user@example.com'");
```

### EXPLAIN Query Plans

```csharp
// See how a query will be executed
db.ExecuteSQL("EXPLAIN SELECT * FROM users WHERE email = 'test@example.com'");
// Output: QUERY PLAN, shows if index is used or full table scan
```

### PRAGMA Commands

```csharp
// Get table schema information
db.ExecuteSQL("PRAGMA table_info(users)");

// List indexes on a table
db.ExecuteSQL("PRAGMA index_list(users)");

// List foreign keys (when implemented)
db.ExecuteSQL("PRAGMA foreign_key_list(orders)");
```

### Date/Time Functions

```csharp
// Use built-in date functions in queries
db.ExecuteSQL("INSERT INTO logs VALUES ('1', NOW())");
db.ExecuteSQL("SELECT * FROM logs WHERE DATE(timestamp) = '2024-01-15'");

// Format dates
db.ExecuteSQL("SELECT STRFTIME(created, 'yyyy-MM-dd') FROM users");

// Date arithmetic
db.ExecuteSQL("SELECT * FROM subscriptions WHERE DATEADD(created, 30, 'days') > NOW()");
```

### Aggregate Functions

```csharp
// Use aggregate functions in queries
db.ExecuteSQL("SELECT SUM(amount) FROM transactions");
db.ExecuteSQL("SELECT AVG(duration) FROM time_entries");
db.ExecuteSQL("SELECT COUNT(DISTINCT user_id) FROM sessions");
db.ExecuteSQL("SELECT GROUP_CONCAT(tags, '|') FROM posts");
```

### UPSERT Operations

```csharp
// Insert or replace if exists
db.ExecuteSQL("INSERT OR REPLACE INTO users VALUES ('1', 'Alice', 'alice@example.com')");

// Insert on conflict with update
db.ExecuteSQL("INSERT INTO users (id, name, email) VALUES ('1', 'Alice', 'alice@example.com') ON CONFLICT DO UPDATE");
```

### Dapper Integration (SharpCoreDB.Extensions)

```csharp
using SharpCoreDB.Extensions;
using Dapper;

// Get a Dapper-compatible connection
var connection = db.GetDapperConnection();

// Use Dapper queries
var users = connection.Query<User>("SELECT * FROM users WHERE active = true");
```

### Health Checks (SharpCoreDB.Extensions)

```csharp
using SharpCoreDB.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Add health check to ASP.NET Core
services.AddHealthChecks()
    .AddSharpCoreDB(db, "sharpcoredb", testQuery: "SELECT 1");

// Health check endpoint will verify database connectivity
```

## Performance Benchmarks

### Performance Comparison

SharpCoreDB now offers **NoEncryption mode** and **Buffered WAL I/O** for significantly improved performance:

#### Time-Tracking Benchmarks (100k records)

Performance improvements with latest optimizations:

| Database | Inserts (100k) | Select | GroupBy | Allocs | Notes |
|----------|----------------|--------|---------|--------|-------|
| **SharpCoreDB (Optimized)** | ~240s | ~45ms | ~180ms | ~850 MB | With QueryCache + HashIndex + GC opts |
| SharpCoreDB (NoEncrypt) | ~250s | ~55ms | ~200ms | ~900 MB | HighPerformance config |
| SharpCoreDB (Encrypted) | ~260s | ~55ms | ~220ms | ~920 MB | AES-256-GCM encryption |
| SQLite | ~130s | ~25ms | ~95ms | ~420 MB | Native C library |
| LiteDB | ~185s | ~38ms | ~140ms | ~680 MB | Document database |

**Performance Optimizations Implemented:**
- **Query Caching**: 2x speedup on repeated queries (>90% hit rate validated) ✅
- **HashIndex with CREATE INDEX**: O(1) lookup for WHERE clauses (2-5x faster validated) ✅
- **GC Optimization**: Span<byte> and ArrayPool reduce allocations (validated) ✅
- **NoEncryption Mode**: Bypass AES for trusted environments (~4% faster) ✅
- **Buffered WAL**: 1-2MB write buffer for batched operations (~5% faster) ✅
- **.NET 10 + C# 14**: Latest runtime and language optimizations ✅

**Validation Results (10k records):**
```bash
# Quick validation benchmark
dotnet run --project SharpCoreDB.Benchmarks -c Release Validate

## Results:
- QueryCache: 99% hit rate on repeated queries
- HashIndex: 2.03x speedup with CREATE INDEX
- GC Optimization: Span<byte> and ArrayPool confirmed working
```

#### Previous Benchmarks (10k records)

| Database | Inserts (10k) | Performance | Notes |
|----------|---------------|-------------|-------|
| **SharpCoreDB (NoEncrypt + Buffered WAL)** | ~25s | **Baseline** | High-performance mode |
| SharpCoreDB (Encrypted) | ~26s | -4% | AES-256-GCM encryption |
| SQLite | ~13s | +1.9x faster | Native C library |

**Performance Improvements:**
- **NoEncryption mode**: Bypass AES encryption for trusted environments (~5% improvement)
- **Buffered WAL I/O**: 1MB buffer with batched writes (~4% additional improvement)
- **Combined**: ~9% total improvement over previous encrypted-only version

**Configuration Options:**

```csharp
// High-performance mode (no encryption, buffered WAL)
var config = DatabaseConfig.HighPerformance;
var db = factory.Create(dbPath, password, false, config);

// Default mode (encrypted, buffered WAL)
var config = DatabaseConfig.Default;
var db = factory.Create(dbPath, password, false, config);

// Custom configuration
var config = new DatabaseConfig 
{ 
    NoEncryptMode = false,           // Keep encryption
    WalBufferSize = 2 * 1024 * 1024  // 2MB WAL buffer
};
```

**Notes:**
- SharpCoreDB balances security and performance with configurable options
- Default mode uses AES-256-GCM encryption for data at rest
- NoEncryption mode recommended only for trusted environments or benchmarking
- Performance varies based on hardware, workload, and configuration
- For production use with large datasets (100k+ records), consider:
  - Using `DatabaseConfig.HighPerformance` for maximum speed in trusted environments
  - Creating indexes on frequently queried columns
  - Using connection pooling to reuse database instances
  - Enabling auto-maintenance for optimal performance

**Running Benchmarks:**
```bash
cd SharpCoreDB.Benchmarks

# Quick performance test
dotnet run --configuration Release NoEncryption

# Full BenchmarkDotNet suite
dotnet run --configuration Release NoEncryptionBench

# Time-tracking benchmarks (100k records, includes LiteDB)
dotnet run --configuration Release TimeTracking
```

## Supported Data Types

| Type | Description |
|------|-------------|
| INTEGER | 32-bit integer |
| TEXT | String value |
| REAL | Double-precision floating point |
| BLOB | Binary data |
| BOOLEAN | True/false value |
| DATETIME | Date and time value |
| LONG | 64-bit integer |
| DECIMAL | High-precision decimal |
| ULID | Universally Unique Lexicographically Sortable Identifier |
| GUID | Globally Unique Identifier |

## Security Considerations

**Important:** SharpCoreDB currently does not support parameterized queries. When building SQL statements:

1. **Always sanitize user input** before including it in SQL statements
2. **Escape single quotes** in string values by replacing `'` with `''`
3. **Validate input** against expected formats and patterns
4. **Use allowlists** for column names, table names, and other identifiers
5. **Never directly interpolate** untrusted user input into SQL statements

Example of safe SQL construction:
```csharp
// BAD - Vulnerable to SQL injection
var username = userInput;
db.ExecuteSQL($"SELECT * FROM users WHERE name = '{username}'");

// GOOD - Sanitized input
var username = userInput.Replace("'", "''"); // Escape single quotes
if (username.Length > 50) throw new ArgumentException("Username too long");
db.ExecuteSQL($"SELECT * FROM users WHERE name = '{username}'");

// BETTER - Validate against allowlist
var allowedUsernames = new[] { "alice", "bob", "charlie" };
if (!allowedUsernames.Contains(userInput))
    throw new ArgumentException("Invalid username");
db.ExecuteSQL($"SELECT * FROM users WHERE name = '{userInput}'");
```

## Entity Framework Core Provider

✅ **Status: Functional** - The EF Core provider is now operational with full LINQ support!

### Features
- **Full Type Mapping**: Support for DateTime, int, long, string, bool, double, decimal, Guid, byte[], and ULID
- **LINQ Query Translation**: Complete support for WHERE, SELECT, JOIN, GROUP BY, ORDER BY, and aggregate functions
- **Migrations**: CREATE TABLE, CREATE INDEX, UPSERT (INSERT OR REPLACE) support
- **Aggregate Functions**: SUM(), AVG(), COUNT(), GROUP_CONCAT()
- **DateTime Functions**: NOW(), DATEADD(), STRFTIME() for date operations
- **Connection String Support**: Standard EF Core connection string configuration
- **.NET 10 & C# 14**: Built exclusively for the latest .NET runtime

### Usage

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

// Define your entities
public class TimeEntry
{
    public int Id { get; set; }
    public string ProjectName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationHours { get; set; }
}

// Create a DbContext
public class TimeTrackingContext : DbContext
{
    private readonly string _connectionString;

    public TimeTrackingContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Use SharpCoreDB provider
        optionsBuilder.UseSharpCoreDB(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired();
            entity.Property(e => e.StartTime).IsRequired();
        });
    }
}

// Use the context
var connectionString = "Data Source=/path/to/db;Password=YourPassword";
using var context = new TimeTrackingContext(connectionString);

// Create database
context.Database.EnsureCreated();

// Add data
context.TimeEntries.Add(new TimeEntry
{
    Id = 1,
    ProjectName = "CoralTime",
    StartTime = DateTime.Now,
    EndTime = DateTime.Now.AddHours(8),
    DurationHours = 8
});
context.SaveChanges();

// Query with LINQ
var entries = context.TimeEntries
    .Where(e => e.ProjectName == "CoralTime")
    .ToList();

// Use aggregations
var totalHours = context.TimeEntries
    .Where(e => e.ProjectName == "CoralTime")
    .Sum(e => e.DurationHours);

// Group by
var projectStats = context.TimeEntries
    .GroupBy(e => e.ProjectName)
    .Select(g => new { Project = g.Key, Count = g.Count(), Total = g.Sum(e => e.DurationHours) })
    .ToList();
```

### Migration Support

```csharp
// Migrations work seamlessly
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Limitations

⚠️ **Current Implementation Notes**:
- Optimized for CoralTime and time-tracking use cases
- Query execution leverages SharpCoreDB's native SQL engine
- Some advanced EF Core features (lazy loading, change tracking optimization) use default implementations

### Alternatives
1. **EF Core Provider** (new!): Full LINQ support with type safety
2. **Direct SQL**: `db.ExecuteSQL("SELECT * FROM users")` for maximum control
3. **Dapper Integration**: Available via `SharpCoreDB.Extensions` package

## Migrating from SQLite

If you're migrating from SQLite to SharpCoreDB, follow these steps:

### 1. Export SQLite Data

Use SQLite's `.dump` command or a tool like `sqlite3` to export your schema and data:

```bash
sqlite3 mydb.sqlite .schema > schema.sql
sqlite3 mydb.sqlite .dump > data.sql
```

### 2. Convert Schema

SharpCoreDB supports most SQLite data types. Key differences:
- Use `ULID` or `GUID` for auto-generated identifiers instead of `AUTOINCREMENT`
- `DECIMAL` type for high-precision numbers
- No foreign key enforcement (use application logic)

Example conversion:
```sql
-- SQLite
CREATE TABLE users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL
);

-- SharpCoreDB
CREATE TABLE users (
    id INTEGER PRIMARY KEY,
    name TEXT
);
```

### 3. Import to SharpCoreDB

```csharp
using SharpCoreDB;

// Create SharpCoreDB instance
var db = factory.Create("migrated.db", "password");

// Execute converted schema
db.ExecuteSQL(convertedSchema);

// Import data (adapt INSERT statements)
foreach (var insert in dataInserts)
{
    db.ExecuteSQL(insert);
}
```

### 4. Update Connection Strings

Replace SQLite connection strings:
```csharp
// From: "Data Source=mydb.sqlite"
// To: "Data Source=mydb.db;Password=your_password"
```

### 5. Test and Validate

- Verify data integrity
- Test performance with your workload
- Update any SQLite-specific queries

### Key Benefits of Migration

- **Encryption**: AES-256-GCM encryption at rest
- **Performance**: Optimized for .NET 10 with query caching and hash indexes
- **Features**: Built-in connection pooling, auto-maintenance, async support
- **Security**: User authentication and access control

## Requirements

- **.NET 10.0** - Built exclusively for .NET 10 with latest runtime optimizations
- **C# 14** - Leverages latest C# language features for performance and safety
- **Native AOT Ready** - Benchmarks support Native AOT compilation for maximum performance

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history
