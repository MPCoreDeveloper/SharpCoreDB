# SharpCoreDB Usage Guide for Developers

> A comprehensive guide for integrating and using SharpCoreDB in your .NET 10+ applications

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14.0-green)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

---

## Table of Contents

- [Introduction](#introduction)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Basic Usage](#basic-usage)
  - [Database Initialization](#database-initialization)
  - [Creating Tables](#creating-tables)
  - [Data Types](#data-types)
  - [CRUD Operations](#crud-operations)
- [Advanced Features](#advanced-features)
  - [Prepared Statements](#prepared-statements)
  - [Transactions & Batching](#transactions--batching)
  - [Query Performance](#query-performance)
  - [Indexes](#indexes)
  - [Compaction & VACUUM](#compaction--vacuum)
  - [Encryption Options](#encryption-options)
- [Security & Access Control](#security--access-control)
  - [User Management](#user-management)
  - [Read-Only Users](#read-only-users)
  - [Database Permissions](#database-permissions)
- [Performance Tuning](#performance-tuning)
- [Integration Patterns](#integration-patterns)
- [API Reference](#api-reference)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

---

## Introduction

**SharpCoreDB** is a lightweight, encrypted, file-based database engine for .NET 10 with SQL support, AES-256-GCM encryption, and modern C# 14 features. It's designed for:

- **Time-tracking applications**
- **Invoicing systems**
- **Project management tools**
- **Embedded database scenarios**
- **Applications requiring data encryption at rest**

### Key Features

✅ **Full SQL Support** - CREATE, INSERT, SELECT, UPDATE, DELETE, JOIN  
✅ **AES-256-GCM Encryption** - Military-grade encryption by default  
✅ **Zero External Dependencies** - No SQL server required  
✅ **ACID Transactions** - Write-Ahead Logging (WAL)  
✅ **Rich Data Types** - INTEGER, TEXT, REAL, DECIMAL, DATETIME, BOOLEAN, ULID, GUID  
✅ **High Performance** - Query caching, hash indexes, optimized storage  
✅ **User Management** - Built-in authentication & read-only mode  

---

## Installation

### NuGet Package

```bash
dotnet add package SharpCoreDB
```

### Manual Installation

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB
dotnet build
```

---

## Quick Start

### Minimal Example (5 Lines)

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

// 1. Setup dependency injection
var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

// 2. Create database
var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var db = factory.Create("./myapp_db", masterPassword: "MySecurePassword123!");

// 3. Create table
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");

// 4. Insert data
db.ExecuteSQL("INSERT INTO users VALUES (@0, @1, @2)", 
    new Dictionary<string, object?> {
        { "0", 1 },
        { "1", "Alice" },
        { "2", "alice@example.com" }
    });

// 5. Query data
var results = db.ExecuteQuery("SELECT * FROM users WHERE id = @0", 
    new Dictionary<string, object?> { { "0", 1 } });

foreach (var row in results)
{
    Console.WriteLine($"{row["id"]}: {row["name"]} ({row["email"]})");
}
```

**Output:**
```
1: Alice (alice@example.com)
```

---

## Core Concepts

### Database Architecture

```
┌─────────────────────────────────────────────┐
│          Your Application                   │
└──────────────────┬──────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────┐
│         SharpCoreDB API                     │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │ SQL      │  │ Crypto   │  │ User     │  │
│  │ Parser   │  │ Service  │  │ Service  │  │
│  └──────────┘  └──────────┘  └──────────┘  │
└──────────────────┬──────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────┐
│        Storage Layer                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │ WAL      │  │ Page     │  │ Hash     │  │
│  │ Manager  │  │ Cache    │  │ Indexes  │  │
│  └──────────┘  └──────────┘  └──────────┘  │
└──────────────────┬──────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────┐
│      File System (Encrypted .dat files)     │
└─────────────────────────────────────────────┘
```

### Connection Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| **Read-Write** | Full CRUD operations | Primary application database |
| **Read-Only** | SELECT queries only | Reporting, analytics, auditing |
| **No-Encrypt** | Unencrypted storage | High-performance, trusted environments |

---

## Basic Usage

### Database Initialization

#### Standard Initialization

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var db = factory.Create(
    dbPath: "./app_database",
    masterPassword: "MySecurePassword123!"
);
```

#### With Configuration

```csharp
var config = new DatabaseConfig
{
    EnableQueryCache = true,
    QueryCacheSize = 2000,
    EnablePageCache = true,
    PageCacheCapacity = 10000,
    WalDurabilityMode = DurabilityMode.FullSync
};

var db = factory.Create("./app_database", "MyPassword", config: config);
```

#### Read-Only Mode

```csharp
var dbReadOnly = factory.Create(
    "./app_database", 
    "MyPassword", 
    isReadOnly: true
);

// This works ✅
var results = dbReadOnly.ExecuteQuery("SELECT * FROM users");

// This throws InvalidOperationException ❌
dbReadOnly.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
```

---

### Creating Tables

#### Basic Table

```csharp
db.ExecuteSQL(@"
    CREATE TABLE products (
        id INTEGER PRIMARY KEY,
        name TEXT,
        price DECIMAL,
        stock INTEGER,
        active BOOLEAN
    )
");
```

#### With Auto-Generated Fields

```csharp
db.ExecuteSQL(@"
    CREATE TABLE orders (
        order_id INTEGER PRIMARY KEY,
        customer_name TEXT,
        total_amount DECIMAL,
        order_ulid ULID AUTO,
        session_guid GUID AUTO,
        created_at DATETIME
    )
");
```

#### Storage Mode Selection

```csharp
// Columnar storage (default) - optimized for analytics/SELECT queries
db.ExecuteSQL(@"
    CREATE TABLE analytics_data (
        id INTEGER,
        metric_name TEXT,
        value REAL
    )
");

// Page-based storage - optimized for OLTP/UPDATE-heavy workloads
db.ExecuteSQL(@"
    CREATE TABLE transactions (
        id INTEGER PRIMARY KEY,
        account_id INTEGER,
        amount DECIMAL,
        status TEXT
    ) STORAGE = PAGE_BASED
");
```

---

### Data Types

| Type | C# Type | Example | Description |
|------|---------|---------|-------------|
| `INTEGER` | `int` | `42` | 32-bit integer |
| `LONG` | `long` | `9223372036854775807` | 64-bit integer |
| `TEXT` | `string` | `'Hello World'` | Unicode string |
| `REAL` | `double` | `3.14159` | Floating point |
| `DECIMAL` | `decimal` | `99.99` | Fixed-point decimal |
| `BOOLEAN` | `bool` | `true` / `false` | Boolean value |
| `DATETIME` | `DateTime` | `'2025-01-05T10:30:00'` | Date & time |
| `ULID` | `string` | `01ARZ3NDEKTSV4RRFFQ69G5FAV` | Sortable unique ID |
| `GUID` | `Guid` | `'550e8400-e29b-41d4-a716-446655440000'` | UUID v4 |

#### Type Examples

```csharp
db.ExecuteSQL(@"
    CREATE TABLE type_demo (
        int_col INTEGER,
        long_col LONG,
        text_col TEXT,
        real_col REAL,
        decimal_col DECIMAL,
        bool_col BOOLEAN,
        datetime_col DATETIME,
        ulid_col ULID AUTO,
        guid_col GUID AUTO
    )
");

db.ExecuteSQL("INSERT INTO type_demo VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8)", 
    new Dictionary<string, object?> {
        { "0", 42 },
        { "1", 9223372036854775807L },
        { "2", "Hello" },
        { "3", 3.14159 },
        { "4", 99.99m },
        { "5", true },
        { "6", DateTime.UtcNow },
        { "7", null },  // Auto-generated ULID
        { "8", null }   // Auto-generated GUID
    });
```

---

### CRUD Operations

#### Create (INSERT)

```csharp
// Insert all columns
db.ExecuteSQL("INSERT INTO users VALUES (@0, @1, @2)", 
    new Dictionary<string, object?> {
        { "0", 1 },
        { "1", "Alice" },
        { "2", "alice@example.com" }
    });

// Insert specific columns
db.ExecuteSQL("INSERT INTO users (id, name) VALUES (@0, @1)", 
    new Dictionary<string, object?> {
        { "0", 2 },
        { "1", "Bob" }
    });

// Insert with NULL values
db.ExecuteSQL("INSERT INTO users VALUES (@0, @1, @2)", 
    new Dictionary<string, object?> {
        { "0", 3 },
        { "1", "Charlie" },
        { "2", null }  // NULL email
    });
```

#### Read (SELECT)

```csharp
// Select all
var allUsers = db.ExecuteQuery("SELECT * FROM users");

// Select with WHERE clause
var user = db.ExecuteQuery("SELECT * FROM users WHERE id = @0", 
    new Dictionary<string, object?> { { "0", 1 } });

// Select specific columns
var names = db.ExecuteQuery("SELECT name, email FROM users");

// Select with ORDER BY
var sorted = db.ExecuteQuery("SELECT * FROM users ORDER BY name ASC");

// Select with LIMIT and OFFSET (pagination)
var page1 = db.ExecuteQuery("SELECT * FROM users LIMIT 10 OFFSET 0");
var page2 = db.ExecuteQuery("SELECT * FROM users LIMIT 10 OFFSET 10");
```

#### Update (UPDATE)

```csharp
// Update single record
db.ExecuteSQL("UPDATE users SET email = @0 WHERE id = @1", 
    new Dictionary<string, object?> {
        { "0", "newemail@example.com" },
        { "1", 1 }
    });

// Update multiple columns
db.ExecuteSQL("UPDATE users SET name = @0, email = @1 WHERE id = @2", 
    new Dictionary<string, object?> {
        { "0", "Alice Updated" },
        { "1", "alice.new@example.com" },
        { "2", 1 }
    });

// Update all records (⚠️ use with caution)
db.ExecuteSQL("UPDATE users SET active = @0", 
    new Dictionary<string, object?> { { "0", true } });
```

#### Delete (DELETE)

```csharp
// Delete specific record
db.ExecuteSQL("DELETE FROM users WHERE id = @0", 
    new Dictionary<string, object?> { { "0", 1 } });

// Delete with complex WHERE clause
db.ExecuteSQL("DELETE FROM users WHERE active = @0 AND id > @1", 
    new Dictionary<string, object?> {
        { "0", false },
        { "1", 100 }
    });

// Delete all (⚠️ use with extreme caution)
db.ExecuteSQL("DELETE FROM users");
```

---

## Advanced Features

### Prepared Statements

For queries executed repeatedly with different parameters, use prepared statements for better performance:

```csharp
// Prepare once
var stmt = db.Prepare("INSERT INTO users VALUES (@0, @1, @2)");

// Execute multiple times with different parameters
for (int i = 1; i <= 1000; i++)
{
    db.ExecutePrepared(stmt, new Dictionary<string, object?> {
        { "0", i },
        { "1", $"User{i}" },
        { "2", $"user{i}@example.com" }
    });
}
```

**Performance Gain:** ~15-25% faster for repeated queries

### Transactions & Batching

#### Batch SQL Execution

```csharp
var sqlStatements = new List<string>
{
    "INSERT INTO users VALUES (1, 'Alice', 'alice@example.com')",
    "INSERT INTO users VALUES (2, 'Bob', 'bob@example.com')",
    "INSERT INTO users VALUES (3, 'Charlie', 'charlie@example.com')"
};

db.ExecuteBatchSQL(sqlStatements);
```

#### Async Batch Processing

```csharp
var statements = Enumerable.Range(1, 10000)
    .Select(i => $"INSERT INTO users VALUES ({i}, 'User{i}', 'user{i}@example.com')");

await db.ExecuteBatchSQLAsync(statements);
```

### Query Performance

#### Query Cache

```csharp
var config = new DatabaseConfig
{
    EnableQueryCache = true,
    QueryCacheSize = 2000  // Cache up to 2000 queries
};

var db = factory.Create("./mydb", "password", config: config);

// First execution: Cache MISS
var results1 = db.ExecuteQuery("SELECT * FROM users WHERE id = @0", 
    new Dictionary<string, object?> { { "0", 1 } });

// Second execution: Cache HIT (much faster)
var results2 = db.ExecuteQuery("SELECT * FROM users WHERE id = @0", 
    new Dictionary<string, object?> { { "0", 1 } });

// Check cache statistics
var stats = db.GetQueryCacheStatistics();
Console.WriteLine($"Hits: {stats.Hits}, Misses: {stats.Misses}, Hit Rate: {stats.HitRate:P2}");
```

#### Page Cache

```csharp
var config = new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 10000,  // Cache 10,000 pages (40MB at 4KB/page)
    PageSize = 4096
};

var db = factory.Create("./mydb", "password", config: config);
```

**Performance Gain:** 5-10x faster for repeated reads

### Indexes

#### Creating Indexes

```csharp
// Create index on single column
db.ExecuteSQL("CREATE INDEX idx_email ON users(email)");

// Create unique index
db.ExecuteSQL("CREATE UNIQUE INDEX idx_username ON users(username)");

// Indexes are created automatically on PRIMARY KEY columns
db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT)");
// → Automatic index on 'id'
```

#### Using Indexes

```csharp
// Query uses index (fast O(1) lookup)
var user = db.ExecuteQuery("SELECT * FROM users WHERE email = @0", 
    new Dictionary<string, object?> { { "0", "alice@example.com" } });

// Query without index (full table scan)
var results = db.ExecuteQuery("SELECT * FROM users WHERE name LIKE @0", 
    new Dictionary<string, object?> { { "0", "%Alice%" } });
```

#### Managing Indexes

```csharp
// Drop index
db.ExecuteSQL("DROP INDEX idx_email");

// Drop index if exists (no error if missing)
db.ExecuteSQL("DROP INDEX IF EXISTS idx_email");
```

### Compaction & VACUUM

Columnar (append-only) storage uses append semantics for UPDATE and logical deletes for DELETE. Over time, this can leave stale versions and deleted rows on disk. To reclaim space and keep scans fast, use compaction.

- Auto-compaction
  - Triggers automatically after a threshold of UPDATE/DELETE operations (default: 1000)
  - Runs in the background and is safe to ignore if it fails

- Manual compaction (SQL)
  - Use `VACUUM table_name` to compact a specific columnar table
  - Rebuilds the primary key index and loaded hash indexes after compaction

Examples:

```
-- Compact a single table
VACUUM analytics_events;

-- Typical workflow
UPDATE analytics_events SET status = 'closed' WHERE id < 1000;
DELETE FROM analytics_events WHERE archived = 'true';
VACUUM analytics_events;  -- reclaim disk space
```

Notes:
- Compaction currently requires a PRIMARY KEY to compute active rows
- Page-based storage does in-place UPDATE/DELETE and does not require VACUUM
- After compaction, positions change; indexes are rebuilt automatically

### Encryption Options

#### Default (AES-256-GCM Encryption)

```csharp
var db = factory.Create("./secure_db", "StrongPassword123!");
// All data is encrypted at rest with AES-256-GCM
```

#### No Encryption Mode (High Performance)

```csharp
var config = new DatabaseConfig
{
    NoEncryptMode = true  // ⚠️ Use only in trusted environments
};

var db = factory.Create("./fast_db", "password", config: config);
```

**Performance Gain:** ~20-30% faster, but **NO ENCRYPTION**

⚠️ **Security Warning:** Only use `NoEncryptMode` in:
- Local development
- Trusted networks
- Non-sensitive data
- Performance benchmarks

---

## Security & Access Control

### User Management

SharpCoreDB includes built-in user authentication with password hashing.

#### Creating Users

```csharp
// Create a user
db.CreateUser("admin", "AdminPassword123!");
db.CreateUser("readonly_user", "ReadOnlyPass456!");
```

#### User Login

```csharp
// Authenticate user
bool isAuthenticated = db.Login("admin", "AdminPassword123!");

if (isAuthenticated)
{
    Console.WriteLine("✅ Login successful!");
}
else
{
    Console.WriteLine("❌ Invalid credentials!");
}
```

### Read-Only Users

Create read-only database connections for users who should only query data:

```csharp
// Admin creates the database and tables
var adminDb = factory.Create("./app_db", "AdminPassword");
adminDb.ExecuteSQL("CREATE TABLE invoices (id INTEGER, amount DECIMAL, client TEXT)");
adminDb.ExecuteSQL("INSERT INTO invoices VALUES (1, 1500.00, 'Acme Corp')");

// Read-only user connects (different database instance)
var readOnlyDb = factory.Create("./app_db", "AdminPassword", isReadOnly: true);

// ✅ Queries work
var invoices = readOnlyDb.ExecuteQuery("SELECT * FROM invoices");
Console.WriteLine($"Found {invoices.Count} invoices");

// ❌ Modifications throw InvalidOperationException
try
{
    readOnlyDb.ExecuteSQL("INSERT INTO invoices VALUES (2, 2000.00, 'XYZ Inc')");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // Output: "Cannot insert in readonly mode"
}
```

### Database Permissions

#### Recommended Access Pattern

```csharp
public class DatabaseService
{
    private readonly DatabaseFactory _factory;
    private readonly string _dbPath;
    private readonly string _masterPassword;

    public IDatabase GetAdminConnection()
    {
        return _factory.Create(_dbPath, _masterPassword, isReadOnly: false);
    }

    public IDatabase GetReadOnlyConnection()
    {
        return _factory.Create(_dbPath, _masterPassword, isReadOnly: true);
    }
}

// Usage
var service = new DatabaseService(factory, "./app_db", "MasterPassword");

// Admin operations
using (var adminDb = service.GetAdminConnection())
{
    adminDb.ExecuteSQL("CREATE TABLE ...");
    adminDb.ExecuteSQL("INSERT INTO ...");
}

// Reporting/analytics (read-only)
using (var readerDb = service.GetReadOnlyConnection())
{
    var report = readerDb.ExecuteQuery("SELECT SUM(amount) FROM invoices");
}
```

---

## Performance Tuning

### Configuration Presets

#### Development (Default)

```csharp
var config = DatabaseConfig.Default;
// Encryption: Enabled
// Query Cache: Enabled (1024 queries)
// Page Cache: Enabled (1000 pages)
```

#### Production (High Performance)

```csharp
var config = DatabaseConfig.HighPerformance;
// Encryption: Disabled (NoEncryptMode = true)
// Query Cache: Enabled (2000 queries)
// Page Cache: Enabled (10000 pages)
// WAL: Group Commit with Async durability
// Buffer Pool: 64MB
```

#### Bulk Import

```csharp
var config = DatabaseConfig.BulkImport;
// Optimized for large INSERT operations
// Group Commit: 5000 rows per batch
// WAL Buffer: 16MB
// Buffer Pool: 256MB
```

### Optimization Strategies

#### 1. Batch Inserts

```csharp
// ❌ Slow (1000 individual inserts)
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}')");
}

// ✅ Fast (batched)
var statements = Enumerable.Range(0, 1000)
    .Select(i => $"INSERT INTO users VALUES ({i}, 'User{i}')");
db.ExecuteBatchSQL(statements);
```

#### 2. Use Indexes Strategically

```csharp
// Create indexes on frequently queried columns
db.ExecuteSQL("CREATE INDEX idx_email ON users(email)");
db.ExecuteSQL("CREATE INDEX idx_created_at ON orders(created_at)");

// Avoid over-indexing (slows down INSERTs/UPDATEs)
```

#### 3. Enable Query Cache for Repeated Queries

```csharp
var config = new DatabaseConfig
{
    EnableQueryCache = true,
    QueryCacheSize = 2000
};
```

#### 4. Choose Appropriate Storage Mode

```csharp
// Analytics/reporting tables → Columnar (default)
db.ExecuteSQL("CREATE TABLE analytics (date DATE, metric TEXT, value REAL)");

// Transaction tables → Page-based
db.ExecuteSQL(@"
    CREATE TABLE transactions (
        id INTEGER PRIMARY KEY,
        account_id INTEGER,
        amount DECIMAL
    ) STORAGE = PAGE_BASED
");
```

---

## Integration Patterns

### ASP.NET Core Dependency Injection

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB services
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton<DatabaseFactory>();

// Register database instance
builder.Services.AddSingleton<IDatabase>(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    var dbPath = builder.Configuration["Database:Path"] ?? "./app_db";
    var password = builder.Configuration["Database:Password"] 
        ?? throw new InvalidOperationException("Database password not configured");
    
    return factory.Create(dbPath, password, config: DatabaseConfig.HighPerformance);
});

var app = builder.Build();

// Initialize database schema
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDatabase>();
    db.ExecuteSQL("CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT)");
}

app.Run();
```

### Repository Pattern

```csharp
public interface IUserRepository
{
    User? GetById(int id);
    IEnumerable<User> GetAll();
    void Add(User user);
    void Update(User user);
    void Delete(int id);
}

public class UserRepository : IUserRepository
{
    private readonly IDatabase _db;

    public UserRepository(IDatabase db)
    {
        _db = db;
    }

    public User? GetById(int id)
    {
        var results = _db.ExecuteQuery("SELECT * FROM users WHERE id = @0", 
            new Dictionary<string, object?> { { "0", id } });
        
        return results.FirstOrDefault() != null 
            ? MapToUser(results[0]) 
            : null;
    }

    public IEnumerable<User> GetAll()
    {
        var results = _db.ExecuteQuery("SELECT * FROM users");
        return results.Select(MapToUser);
    }

    public void Add(User user)
    {
        _db.ExecuteSQL("INSERT INTO users VALUES (@0, @1, @2)", 
            new Dictionary<string, object?> {
                { "0", user.Id },
                { "1", user.Name },
                { "2", user.Email }
            });
    }

    public void Update(User user)
    {
        _db.ExecuteSQL("UPDATE users SET name = @0, email = @1 WHERE id = @2", 
            new Dictionary<string, object?> {
                { "0", user.Name },
                { "1", user.Email },
                { "2", user.Id }
            });
    }

    public void Delete(int id)
    {
        _db.ExecuteSQL("DELETE FROM users WHERE id = @0", 
            new Dictionary<string, object?> { { "0", id } });
    }

    private static User MapToUser(Dictionary<string, object> row)
    {
        return new User
        {
            Id = Convert.ToInt32(row["id"]),
            Name = row["name"]?.ToString() ?? string.Empty,
            Email = row["email"]?.ToString() ?? string.Empty
        };
    }
}
```

### Unit Testing with xUnit

```csharp
public class UserRepositoryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IDatabase _db;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        
        _db = factory.Create(_testDbPath, "test_password");
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");
        
        _repository = new UserRepository(_db);
    }

    [Fact]
    public void Add_ValidUser_InsertsSuccessfully()
    {
        // Arrange
        var user = new User { Id = 1, Name = "Alice", Email = "alice@example.com" };

        // Act
        _repository.Add(user);

        // Assert
        var retrieved = _repository.GetById(1);
        Assert.NotNull(retrieved);
        Assert.Equal("Alice", retrieved.Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }
}
```

---

## API Reference

### IDatabase Interface

#### Core Methods

```csharp
void ExecuteSQL(string sql);
void ExecuteSQL(string sql, Dictionary<string, object?> parameters);
Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default);
Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);
```

#### Query Methods

```csharp
List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null);
List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt);
```

#### Batch Methods

```csharp
void ExecuteBatchSQL(IEnumerable<string> sqlStatements);
Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default);
```

#### Prepared Statements

```csharp
PreparedStatement Prepare(string sql);
void ExecutePrepared(PreparedStatement stmt, Dictionary<string, object?> parameters);
Task ExecutePreparedAsync(PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);
```

#### User Management

```csharp
void CreateUser(string username, string password);
bool Login(string username, string password);
```

#### Cache Management

```csharp
(long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics();
void ClearQueryCache();
```

### DatabaseConfig Properties

```csharp
bool NoEncryptMode { get; init; }              // Disable encryption for performance
bool EnableQueryCache { get; init; }           // Enable query result caching
int QueryCacheSize { get; init; }              // Max cached queries (default: 1024)
bool EnablePageCache { get; init; }            // Enable page-level caching
int PageCacheCapacity { get; init; }           // Max cached pages (default: 1000)
int PageSize { get; init; }                    // Page size in bytes (default: 4096)
DurabilityMode WalDurabilityMode { get; init; } // WAL sync mode (FullSync/Async)
bool UseGroupCommitWal { get; init; }          // Enable group commit optimization
```

### DatabaseFactory Methods

```csharp
IDatabase Create(
    string dbPath,
    string masterPassword,
    bool isReadOnly = false,
    DatabaseConfig? config = null,
    SecurityConfig? securityConfig = null
);
```

---

## Best Practices

### 1. Always Use Parameterized Queries

```csharp
// ❌ BAD (SQL injection risk)
db.ExecuteSQL($"SELECT * FROM users WHERE name = '{userInput}'");

// ✅ GOOD (safe from injection)
db.ExecuteSQL("SELECT * FROM users WHERE name = @0", 
    new Dictionary<string, object?> { { "0", userInput } });
```

### 2. Use Read-Only Connections for Queries

```csharp
// ❌ BAD (uses read-write connection for read-only operations)
var results = adminDb.ExecuteQuery("SELECT * FROM reports");

// ✅ GOOD (uses read-only connection)
using var readerDb = factory.Create(dbPath, password, isReadOnly: true);
var results = readerDb.ExecuteQuery("SELECT * FROM reports");
```

### 3. Dispose Database Instances Properly

```csharp
// ✅ GOOD (automatic disposal)
using (var db = factory.Create(dbPath, password))
{
    db.ExecuteSQL("CREATE TABLE ...");
}

// ✅ GOOD (manual disposal)
var db = factory.Create(dbPath, password);
try
{
    db.ExecuteSQL("CREATE TABLE ...");
}
finally
{
    db.Dispose();
}
```

### 4. Choose Appropriate Configuration

```csharp
// Development → Default config
var devDb = factory.Create(dbPath, password);

// Production → High performance config
var prodDb = factory.Create(dbPath, password, config: DatabaseConfig.HighPerformance);

// Bulk import → Optimized for inserts
var importDb = factory.Create(dbPath, password, config: DatabaseConfig.BulkImport);
```

### 5. Handle Exceptions Gracefully

```csharp
try
{
    db.ExecuteSQL("INSERT INTO users VALUES (@0, @1)", 
        new Dictionary<string, object?> { { "0", 1 }, { "1", "Alice" } });
}
catch (InvalidOperationException ex)
{
    // Handle database-specific errors
    Console.WriteLine($"Database error: {ex.Message}");
}
catch (Exception ex)
{
    // Handle unexpected errors
    Console.WriteLine($"Unexpected error: {ex.Message}");
}
```

### 6. Use Indexes Wisely

```csharp
// ✅ Index frequently queried columns
db.ExecuteSQL("CREATE INDEX idx_email ON users(email)");

// ✅ Index foreign key columns for JOINs
db.ExecuteSQL("CREATE INDEX idx_user_id ON orders(user_id)");

// ❌ Don't over-index (slows down writes)
// Only index columns used in WHERE/JOIN/ORDER BY clauses
```

### 7. Optimize Bulk Operations

```csharp
// ❌ Slow (1000 individual queries)
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}')");
}

// ✅ Fast (batched)
var statements = Enumerable.Range(0, 1000)
    .Select(i => $"INSERT INTO users VALUES ({i}, 'User{i}')");
db.ExecuteBatchSQL(statements);

// ✅ Even faster (prepared statement)
var stmt = db.Prepare("INSERT INTO users VALUES (@0, @1)");
for (int i = 0; i < 1000; i++)
{
    db.ExecutePrepared(stmt, new Dictionary<string, object?> {
        { "0", i },
        { "1", $"User{i}" }
    });
}
```

---

## Troubleshooting

### Common Issues

#### 1. "Cannot insert in readonly mode"

**Problem:** Trying to modify data with a read-only connection.

**Solution:**
```csharp
// Create read-write connection
var db = factory.Create(dbPath, password, isReadOnly: false);
```

#### 2. "Table X does not exist"

**Problem:** Querying a table that hasn't been created.

**Solution:**
```csharp
// Create table first
db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");

// Then query
var results = db.ExecuteQuery("SELECT * FROM users");
```

#### 3. Slow Query Performance

**Problem:** Queries taking too long.

**Solutions:**
```csharp
// 1. Add indexes
db.ExecuteSQL("CREATE INDEX idx_email ON users(email)");

// 2. Enable query cache
var config = new DatabaseConfig { EnableQueryCache = true };

// 3. Enable page cache
var config = new DatabaseConfig { EnablePageCache = true, PageCacheCapacity = 10000 };

// 4. Use prepared statements for repeated queries
var stmt = db.Prepare("SELECT * FROM users WHERE id = @0");
```

#### 4. High Memory Usage

**Problem:** Application consuming too much memory.

**Solutions:**
```csharp
// 1. Reduce page cache capacity
var config = new DatabaseConfig { PageCacheCapacity = 1000 };

// 2. Reduce query cache size
var config = new DatabaseConfig { QueryCacheSize = 500 };

// 3. Clear cache periodically
db.ClearQueryCache();
```

#### 5. File Locking Issues

**Problem:** "File is in use by another process"

**Solution:**
```csharp
// Ensure proper disposal
using (var db = factory.Create(dbPath, password))
{
    // Use database
}
// Database is disposed and files are released here
```

---

## Additional Resources

- **GitHub Repository:** https://github.com/MPCoreDeveloper/SharpCoreDB
- **NuGet Package:** https://www.nuget.org/packages/SharpCoreDB
- **Issue Tracker:** https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- **License:** MIT License

---

## License

```
MIT License

Copyright (c) 2025-2026 MPCoreDeveloper

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

**Need Help?** Open an issue on [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB/issues) or check the [documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/wiki).
