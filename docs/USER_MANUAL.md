# SharpCoreDB User Manual

**Complete Guide for Developers Integrating SharpCoreDB**

**Version**: 1.0  
**Last Updated**: February 2026  
**Target Audience**: Software developers integrating SharpCoreDB into applications

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Core Concepts](#core-concepts)
3. [Database Setup & Configuration](#database-setup--configuration)
4. [SQL Features by Category](#sql-features-by-category)
5. [CRUD Operations](#crud-operations)
6. [Advanced Features](#advanced-features)
7. [Performance Optimization](#performance-optimization)
8. [Integration Patterns](#integration-patterns)
9. [Troubleshooting](#troubleshooting)
10. [Best Practices](#best-practices)

---

## Getting Started

### Installation

SharpCoreDB is available on NuGet:

```bash
dotnet add package SharpCoreDB
```

Or in your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="SharpCoreDB" Version="1.0.0" />
</ItemGroup>
```

### Prerequisites

- **.NET 10** or later
- **C# 14** language features
- **Windows, Linux, macOS** compatible
- **500MB** disk space minimum for full feature set

### Quick Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// 1. Register with Dependency Injection
var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();

// 2. Get database factory
var factory = provider.GetRequiredService<DatabaseFactory>();

// 3. Create or open database
using var db = factory.Create(
    path: "./myapp_db",
    password: "YourStrongPassword123!"
);

// 4. Create schema
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT)");

// 5. Insert data
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com')");

// 6. Query data
var rows = db.ExecuteQuery("SELECT * FROM users WHERE id = 1");

// 7. Flush and save
db.Flush();
db.ForceSave();
```

---

## Core Concepts

### What is SharpCoreDB?

SharpCoreDB is a **high-performance embedded database engine** for .NET applications:

- **Embedded**: Runs in-process, no server needed
- **Relational**: Full SQL support with indexes, constraints, and transactions
- **Encrypted**: AES-256-GCM encryption with zero overhead
- **Fast**: 43% faster INSERTs than SQLite, 2.3x faster SELECTs than LiteDB
- **SIMD-Accelerated**: 28,660x faster analytics than LiteDB
- **Extensible**: Stored procedures, views, triggers, time-series support

### Storage Engines

SharpCoreDB supports multiple storage engines optimized for different workloads:

| Engine | Use Case | Characteristics |
|--------|----------|-----------------|
| **PageBased** (OLTP) | General-purpose | Optimized for random access, transactions |
| **Columnar** | Analytics | SIMD-accelerated, efficient aggregations |
| **AppendOnly** | Logging | Optimized for sequential writes |
| **SCDB** | Enterprise | Block-based, with row overflow, FILESTREAM |

Default: **PageBased** (switch via `DatabaseOptions`)

### Encryption

All data is encrypted at rest with **AES-256-GCM**:

```csharp
// Encryption is automatic when you provide a password
using var db = factory.Create("./db", "password");
// ✅ All data is encrypted
```

No configuration needed — encryption is **transparent and adds 0% overhead**!

### Persistence

SharpCoreDB uses **Write-Ahead Logging (WAL)** for crash recovery:

```csharp
// Data is written to WAL first, then applied
db.ExecuteSQL("INSERT INTO users VALUES (...)");

// To ensure data is written to disk:
db.Flush();        // Flushes WAL
db.ForceSave();    // Forces checkpoint to main database file

// For maximum durability:
db.ForceSave();
db.Flush();
```

---

## Database Setup & Configuration

### Basic Creation

```csharp
// In-memory database (useful for testing)
using var db = factory.CreateInMemory("TestDB");

// File-based with encryption
using var db = factory.Create("./data/mydb", "password123");

// Custom configuration
var options = new DatabaseOptions
{
    StorageMode = StorageMode.PageBased,  // or Columnar, AppendOnly, SCDB
    PageSize = 4096,
    EnableWal = true,
    WalSegmentSize = 1024 * 1024,  // 1 MB
    CacheSize = 256,  // 256 pages
    MaxConnections = 1,  // Embedded DB, usually 1
};
using var db = factory.Create("./data/mydb", "password", options);
```

### Dependency Injection Setup

For ASP.NET Core applications:

```csharp
// Startup.cs or Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB
builder.Services.AddSharpCoreDB();

// Get the factory in controllers/services
public class UserService
{
    private readonly DatabaseFactory _factory;
    
    public UserService(DatabaseFactory factory)
    {
        _factory = factory;
    }
    
    public async Task AddUserAsync(string name)
    {
        using var db = _factory.Create("./app.db", "pwd");
        db.ExecuteSQL($"INSERT INTO users VALUES ('{name}')");
        db.Flush();
    }
}
```

### Connection Management

```csharp
// Single connection (recommended for embedded DB)
using var db = factory.Create("./db", "pwd");

// Important: Always use 'using' or call Dispose()
using (var db = factory.Create("./db", "pwd"))
{
    // Operations
    db.ExecuteSQL("...");
}  // ✅ Database properly closed here

// Async operations
public async Task DoWorkAsync()
{
    using var db = factory.Create("./db", "pwd");
    await ProcessAsync(db);
}
```

---

## SQL Features by Category

### DDL (Data Definition Language)

#### CREATE TABLE

```csharp
// Basic table with PRIMARY KEY
db.ExecuteSQL(@"
    CREATE TABLE users (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        email TEXT UNIQUE,
        age INTEGER,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )
");

// With constraints
db.ExecuteSQL(@"
    CREATE TABLE orders (
        id INTEGER PRIMARY KEY,
        user_id INTEGER,
        total_amount REAL NOT NULL,
        status TEXT CHECK (status IN ('pending', 'completed', 'cancelled')),
        FOREIGN KEY (user_id) REFERENCES users(id)
    )
");

// Supported Data Types
// - INTEGER (int, int64, long)
// - REAL (float, double)
// - TEXT (string, char)
// - BLOB (byte[])
// - BOOLEAN (bool)
// - TIMESTAMP (DateTime)
// - GUID (Guid)
// - ULID (ULIDs for distributed systems)
```

#### CREATE INDEX

```csharp
// Hash index (O(1) point lookups)
db.ExecuteSQL("CREATE INDEX idx_email ON users(email) USING HASH");

// B-tree index (O(log n) range queries, ORDER BY, BETWEEN)
db.ExecuteSQL("CREATE INDEX idx_age ON users(age) USING BTREE");

// Composite index
db.ExecuteSQL("CREATE INDEX idx_user_order ON orders(user_id, created_at) USING BTREE");

// Partial index (specific rows)
db.ExecuteSQL("CREATE INDEX idx_active_users ON users(name) USING BTREE WHERE age > 18");
```

#### ALTER TABLE

```csharp
// Add column
db.ExecuteSQL("ALTER TABLE users ADD COLUMN phone TEXT");

// Drop column
db.ExecuteSQL("ALTER TABLE users DROP COLUMN phone");

// Rename column
db.ExecuteSQL("ALTER TABLE users RENAME COLUMN phone TO phone_number");
```

#### DROP TABLE

```csharp
db.ExecuteSQL("DROP TABLE users");

// If exists
db.ExecuteSQL("DROP TABLE IF EXISTS users");
```

### DML (Data Manipulation Language)

#### INSERT

```csharp
// Single row insert
db.ExecuteSQL("INSERT INTO users (name, email, age) VALUES ('Alice', 'alice@example.com', 30)");

// With parameters (prevents SQL injection)
db.ExecuteSQL("INSERT INTO users (name, email, age) VALUES (@name, @email, @age)",
    new SqlParameter("@name", "Bob"),
    new SqlParameter("@email", "bob@example.com"),
    new SqlParameter("@age", 25)
);

// Multiple values
db.ExecuteSQL(@"
    INSERT INTO users (name, email, age) VALUES
    ('Alice', 'alice@example.com', 30),
    ('Bob', 'bob@example.com', 25),
    ('Charlie', 'charlie@example.com', 35)
");
```

#### INSERT BATCH (Recommended for many rows)

```csharp
// Batch insert (44% faster than individual inserts!)
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO users VALUES ({i}, 'User{i}', 'user{i}@example.com')");
}
db.ExecuteBatchSQL(statements);  // ✅ Much faster
db.Flush();

// Or use InsertBatch for direct storage engine access
var rows = new List<(int id, string name, string email)>();
for (int i = 0; i < 1000; i++)
{
    rows.Add((i, $"User{i}", $"user{i}@example.com"));
}
var table = db.GetTable("users");
table.InsertBatch(rows.Select(r => new object[] { r.id, r.name, r.email }).ToList());
```

#### SELECT

```csharp
// Simple query
var rows = db.ExecuteQuery("SELECT * FROM users");

// With WHERE clause
var rows = db.ExecuteQuery("SELECT * FROM users WHERE age > 25");

// Specific columns
var rows = db.ExecuteQuery("SELECT name, email FROM users");

// ORDER BY (requires BTREE index for good performance)
var rows = db.ExecuteQuery("SELECT * FROM users ORDER BY age DESC");

// LIMIT and OFFSET (pagination)
var page1 = db.ExecuteQuery("SELECT * FROM users LIMIT 10 OFFSET 0");
var page2 = db.ExecuteQuery("SELECT * FROM users LIMIT 10 OFFSET 10");

// DISTINCT
var cities = db.ExecuteQuery("SELECT DISTINCT city FROM users");

// Aliases
var rows = db.ExecuteQuery("SELECT name AS user_name, email AS user_email FROM users");
```

#### UPDATE

```csharp
// Update single row
db.ExecuteSQL("UPDATE users SET age = 31 WHERE id = 1");

// Update multiple rows
db.ExecuteSQL("UPDATE users SET status = 'active' WHERE age > 18");

// Update with parameters
db.ExecuteSQL("UPDATE users SET age = @age WHERE id = @id",
    new SqlParameter("@age", 32),
    new SqlParameter("@id", 1)
);

// Update multiple columns
db.ExecuteSQL("UPDATE users SET age = 30, status = 'active' WHERE id = 1");
```

#### DELETE

```csharp
// Delete specific rows
db.ExecuteSQL("DELETE FROM users WHERE id = 1");

// Delete with condition
db.ExecuteSQL("DELETE FROM users WHERE age < 18");

// Delete all rows
db.ExecuteSQL("DELETE FROM users");

// Delete all with table truncate (faster)
db.ExecuteSQL("TRUNCATE TABLE users");
```

### Query Features

#### WHERE Clause

```csharp
// Comparison operators
db.ExecuteQuery("SELECT * FROM users WHERE age > 25");
db.ExecuteQuery("SELECT * FROM users WHERE age >= 25");
db.ExecuteQuery("SELECT * FROM users WHERE name = 'Alice'");
db.ExecuteQuery("SELECT * FROM users WHERE email != 'alice@example.com'");

// Logical operators
db.ExecuteQuery("SELECT * FROM users WHERE age > 25 AND city = 'NYC'");
db.ExecuteQuery("SELECT * FROM users WHERE age > 25 OR age < 18");
db.ExecuteQuery("SELECT * FROM users WHERE NOT (age < 18)");

// IN operator
db.ExecuteQuery("SELECT * FROM users WHERE city IN ('NYC', 'LA', 'Chicago')");

// BETWEEN
db.ExecuteQuery("SELECT * FROM users WHERE age BETWEEN 25 AND 35");

// LIKE (pattern matching)
db.ExecuteQuery("SELECT * FROM users WHERE name LIKE 'A%'");    // Starts with A
db.ExecuteQuery("SELECT * FROM users WHERE name LIKE '%ice'");  // Ends with 'ice'
db.ExecuteQuery("SELECT * FROM users WHERE name LIKE '%li%'");  // Contains 'li'

// IS NULL
db.ExecuteQuery("SELECT * FROM users WHERE email IS NULL");
db.ExecuteQuery("SELECT * FROM users WHERE email IS NOT NULL");
```

#### JOIN Operations

```csharp
// INNER JOIN
db.ExecuteQuery(@"
    SELECT users.name, orders.total_amount
    FROM users
    INNER JOIN orders ON users.id = orders.user_id
");

// LEFT JOIN (all from left, matching from right)
db.ExecuteQuery(@"
    SELECT users.name, orders.total_amount
    FROM users
    LEFT JOIN orders ON users.id = orders.user_id
");

// RIGHT JOIN (all from right, matching from left)
db.ExecuteQuery(@"
    SELECT users.name, orders.total_amount
    FROM users
    RIGHT JOIN orders ON users.id = orders.user_id
");

// FULL OUTER JOIN
db.ExecuteQuery(@"
    SELECT users.name, orders.total_amount
    FROM users
    FULL OUTER JOIN orders ON users.id = orders.user_id
");

// CROSS JOIN (cartesian product)
db.ExecuteQuery(@"
    SELECT users.name, products.name
    FROM users
    CROSS JOIN products
");

// Multiple JOINs
db.ExecuteQuery(@"
    SELECT u.name, o.id, i.product_id
    FROM users u
    INNER JOIN orders o ON u.id = o.user_id
    INNER JOIN order_items i ON o.id = i.order_id
");
```

#### Subqueries

```csharp
// Subquery in WHERE
db.ExecuteQuery(@"
    SELECT * FROM users
    WHERE id IN (SELECT user_id FROM orders WHERE total_amount > 100)
");

// Subquery with EXISTS
db.ExecuteQuery(@"
    SELECT * FROM users u
    WHERE EXISTS (SELECT 1 FROM orders o WHERE o.user_id = u.id)
");

// Correlated subquery
db.ExecuteQuery(@"
    SELECT u.name, (SELECT COUNT(*) FROM orders WHERE user_id = u.id) as order_count
    FROM users u
");

// Subquery in FROM clause
db.ExecuteQuery(@"
    SELECT * FROM (
        SELECT user_id, COUNT(*) as order_count
        FROM orders
        GROUP BY user_id
    ) sub
    WHERE order_count > 5
");

// Subquery in SELECT
db.ExecuteQuery(@"
    SELECT 
        name,
        (SELECT COUNT(*) FROM orders WHERE user_id = users.id) as total_orders,
        (SELECT SUM(total_amount) FROM orders WHERE user_id = users.id) as total_spent
    FROM users
");
```

#### Aggregates & GROUP BY

```csharp
// COUNT
db.ExecuteQuery("SELECT COUNT(*) as total_users FROM users");
db.ExecuteQuery("SELECT COUNT(DISTINCT city) as unique_cities FROM users");

// SUM, AVG, MIN, MAX
db.ExecuteQuery("SELECT AVG(age) as avg_age FROM users");
db.ExecuteQuery("SELECT MAX(age) as oldest_user FROM users");
db.ExecuteQuery("SELECT MIN(age) as youngest_user FROM users");
db.ExecuteQuery("SELECT SUM(total_amount) as revenue FROM orders");

// GROUP BY
db.ExecuteQuery(@"
    SELECT city, COUNT(*) as user_count
    FROM users
    GROUP BY city
");

// GROUP BY with multiple columns
db.ExecuteQuery(@"
    SELECT city, age_group, COUNT(*) as count
    FROM users
    GROUP BY city, age_group
");

// HAVING (filter groups)
db.ExecuteQuery(@"
    SELECT city, COUNT(*) as user_count
    FROM users
    GROUP BY city
    HAVING COUNT(*) > 100
");

// Complex aggregation
db.ExecuteQuery(@"
    SELECT 
        city,
        COUNT(*) as user_count,
        AVG(age) as avg_age,
        MAX(age) as max_age,
        MIN(age) as min_age
    FROM users
    GROUP BY city
    HAVING COUNT(*) > 50
    ORDER BY user_count DESC
");
```

---

## CRUD Operations

### Complete CRUD Example

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

public class UserCrudExample
{
    private readonly DatabaseFactory _factory;

    public UserCrudExample(DatabaseFactory factory)
    {
        _factory = factory;
    }

    // CREATE (Insert)
    public void CreateUser(int id, string name, string email, int age)
    {
        using var db = _factory.Create("./app.db", "password");
        
        db.ExecuteSQL("CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER)");
        
        db.ExecuteSQL(
            "INSERT INTO users (id, name, email, age) VALUES (@id, @name, @email, @age)",
            new SqlParameter("@id", id),
            new SqlParameter("@name", name),
            new SqlParameter("@email", email),
            new SqlParameter("@age", age)
        );
        
        db.Flush();
    }

    // READ (Select)
    public dynamic GetUser(int id)
    {
        using var db = _factory.Create("./app.db", "password");
        var rows = db.ExecuteQuery("SELECT * FROM users WHERE id = @id", 
            new SqlParameter("@id", id));
        
        return rows.FirstOrDefault();
    }

    public List<dynamic> GetAllUsers()
    {
        using var db = _factory.Create("./app.db", "password");
        return db.ExecuteQuery("SELECT * FROM users").ToList();
    }

    public List<dynamic> GetUsersByAge(int minAge)
    {
        using var db = _factory.Create("./app.db", "password");
        return db.ExecuteQuery("SELECT * FROM users WHERE age >= @minAge ORDER BY age DESC",
            new SqlParameter("@minAge", minAge)).ToList();
    }

    // UPDATE
    public void UpdateUser(int id, string email, int age)
    {
        using var db = _factory.Create("./app.db", "password");
        
        db.ExecuteSQL(
            "UPDATE users SET email = @email, age = @age WHERE id = @id",
            new SqlParameter("@email", email),
            new SqlParameter("@age", age),
            new SqlParameter("@id", id)
        );
        
        db.Flush();
    }

    // DELETE
    public void DeleteUser(int id)
    {
        using var db = _factory.Create("./app.db", "password");
        
        db.ExecuteSQL("DELETE FROM users WHERE id = @id",
            new SqlParameter("@id", id));
        
        db.Flush();
    }

    // BATCH operations
    public void CreateUsersBatch(List<(int id, string name, string email, int age)> users)
    {
        using var db = _factory.Create("./app.db", "password");
        
        var statements = users.Select(u => 
            $"INSERT INTO users VALUES ({u.id}, '{u.name}', '{u.email}', {u.age})"
        ).ToList();
        
        db.ExecuteBatchSQL(statements);  // ✅ Much faster than individual inserts
        db.Flush();
    }
}
```

---

## Advanced Features

### Stored Procedures

Stored procedures allow you to encapsulate business logic in the database:

```csharp
// CREATE PROCEDURE
db.ExecuteSQL(@"
    CREATE PROCEDURE transfer_funds (
        IN from_account INT,
        IN to_account INT,
        IN amount REAL,
        OUT success INT
    ) AS
    BEGIN
        DECLARE balance REAL;
        SELECT balance FROM accounts WHERE id = from_account INTO balance;
        
        IF balance >= amount THEN
            UPDATE accounts SET balance = balance - amount WHERE id = from_account;
            UPDATE accounts SET balance = balance + amount WHERE id = to_account;
            SET success = 1;
        ELSE
            SET success = 0;
        END IF;
    END
");

// EXEC PROCEDURE
var result = db.ExecuteSQL("EXEC transfer_funds(1, 2, 100.00)");
// OUT parameters are accessible via result dictionary
```

### Views

Views are saved queries that act like virtual tables:

```csharp
// CREATE VIEW
db.ExecuteSQL(@"
    CREATE VIEW active_users AS
    SELECT id, name, email, age
    FROM users
    WHERE age >= 18
");

// Use view in queries
var rows = db.ExecuteQuery("SELECT * FROM active_users WHERE age > 25");

// CREATE MATERIALIZED VIEW (pre-computed results)
db.ExecuteSQL(@"
    CREATE MATERIALIZED VIEW user_stats AS
    SELECT 
        city,
        COUNT(*) as user_count,
        AVG(age) as avg_age
    FROM users
    GROUP BY city
");

// Drop view
db.ExecuteSQL("DROP VIEW active_users");
```

### Triggers

Triggers automatically execute code when data changes:

```csharp
// CREATE TRIGGER - Enforce audit trail
db.ExecuteSQL(@"
    CREATE TRIGGER audit_user_changes
    AFTER UPDATE ON users
    BEGIN
        INSERT INTO audit_log (table_name, action, timestamp, old_data, new_data)
        VALUES ('users', 'UPDATE', CURRENT_TIMESTAMP, OLD.*, NEW.*)
    END
");

// BEFORE INSERT - Validate data
db.ExecuteSQL(@"
    CREATE TRIGGER validate_age
    BEFORE INSERT ON users
    BEGIN
        IF NEW.age < 0 OR NEW.age > 150 THEN
            RAISE ERROR 'Invalid age'
        END IF
    END
");

// Delete trigger
db.ExecuteSQL("DROP TRIGGER audit_user_changes");
```

### Time-Series Data (Phase 8)

SharpCoreDB includes production-grade time-series support with automatic compression:

```csharp
// CREATE time-series table
db.ExecuteSQL(@"
    CREATE TABLE metrics (
        timestamp BIGINT,           -- Unix milliseconds
        sensor_id TEXT,
        temperature REAL,
        humidity REAL,
        pressure REAL,
        PRIMARY KEY (timestamp, sensor_id)
    ) WITH TIMESERIES
");

// INSERT time-series data (automatically compressed)
db.ExecuteSQL(@"
    INSERT INTO metrics VALUES (@ts, @id, @temp, @humidity, @pressure)",
    new SqlParameter("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
    new SqlParameter("@id", "sensor-001"),
    new SqlParameter("@temp", 22.5),
    new SqlParameter("@humidity", 45.0),
    new SqlParameter("@pressure", 1013.25)
);

// Query time-series data (automatic decompression)
var lastHour = db.ExecuteQuery(@"
    SELECT timestamp, sensor_id, temperature
    FROM metrics
    WHERE timestamp > @start_time
    ORDER BY timestamp DESC",
    new SqlParameter("@start_time", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds())
);

// Downsample data (aggregate high-frequency to lower resolution)
var downsampled = db.ExecuteQuery(@"
    SELECT 
        bucket_timestamp(timestamp, 60000) as bucket_time,  -- 1-minute buckets
        sensor_id,
        AVG(temperature) as avg_temp,
        MAX(temperature) as max_temp,
        MIN(temperature) as min_temp,
        COUNT(*) as data_points
    FROM metrics
    WHERE timestamp BETWEEN @start AND @end
    GROUP BY bucket_time, sensor_id
    ORDER BY bucket_time DESC"
);

// Define retention policy (automatic archival)
db.ExecuteSQL(@"
    CREATE RETENTION POLICY old_metrics
    ON metrics
    OLDER_THAN(30 * 24 * 3600 * 1000)  -- 30 days in milliseconds
    AGGREGATE_TO_BUCKET(3600000)  -- 1 hour buckets
");
```

#### Time-Series Compression Codecs

SharpCoreDB uses three industry-standard compression formats:

| Codec | Best For | Compression Ratio |
|-------|----------|------------------|
| **Gorilla** | Floating-point measurements | ~80% reduction |
| **Delta-of-Delta** | Integer timestamps | 90%+ reduction |
| **XOR Float** | IEEE 754 floats | ~70% reduction |

All compression is **automatic and transparent**!

---

## Performance Optimization

### Query Optimization

```csharp
// ✅ DO: Use indexes for WHERE clauses
db.ExecuteSQL("CREATE INDEX idx_email ON users(email) USING BTREE");
db.ExecuteQuery("SELECT * FROM users WHERE email = 'alice@example.com'");

// ✅ DO: Use BTREE indexes for range queries
db.ExecuteSQL("CREATE INDEX idx_age ON users(age) USING BTREE");
db.ExecuteQuery("SELECT * FROM users WHERE age BETWEEN 25 AND 35");

// ❌ DON'T: Use expensive full table scans
db.ExecuteQuery("SELECT * FROM users WHERE name LIKE '%ice%'");  // No index

// ✅ DO: Use batch inserts
db.ExecuteBatchSQL(statements);  // 44% faster

// ✅ DO: Limit returned columns
db.ExecuteQuery("SELECT id, name FROM users");  // Fast

// ❌ DON'T: SELECT *
db.ExecuteQuery("SELECT * FROM users");  // Slower
```

### Batch Operations

```csharp
// Batch Insert (44% faster than individual inserts!)
var statements = new List<string>();
for (int i = 0; i < 10000; i++)
{
    statements.Add($"INSERT INTO users VALUES ({i}, 'User{i}', 'email{i}@example.com')");
}
db.ExecuteBatchSQL(statements);
db.Flush();

// Batch Update
var updateStatements = Enumerable.Range(1, 1000)
    .Select(i => $"UPDATE users SET age = {30 + i} WHERE id = {i}")
    .ToList();
db.ExecuteBatchSQL(updateStatements);
db.Flush();

// Batch Delete
var deleteStatements = Enumerable.Range(1001, 1000)
    .Select(i => $"DELETE FROM users WHERE id = {i}")
    .ToList();
db.ExecuteBatchSQL(deleteStatements);
db.Flush();
```

### Caching

```csharp
// Enable query plan caching (automatic)
// Same query executed multiple times uses cached plan

// Query with parameters (enables plan caching)
db.ExecuteQuery("SELECT * FROM users WHERE age > @age", 
    new SqlParameter("@age", 25));
db.ExecuteQuery("SELECT * FROM users WHERE age > @age", 
    new SqlParameter("@age", 30));  // ✅ Uses cached plan
```

### Asynchronous Operations

```csharp
// SharpCoreDB supports async/await
public async Task ProcessOrdersAsync()
{
    using var db = _factory.Create("./db", "password");
    
    // Async operations allow other work to proceed
    var orders = await Task.Run(() => 
        db.ExecuteQuery("SELECT * FROM orders WHERE status = 'pending'").ToList()
    );
    
    foreach (var order in orders)
    {
        await ProcessOrderAsync(order);
    }
}

// In ASP.NET Core
public async Task<IActionResult> GetUsers()
{
    using var db = _factory.Create("./db", "password");
    var users = await Task.Run(() => 
        db.ExecuteQuery("SELECT * FROM users").ToList()
    );
    return Ok(users);
}
```

### Large Data Handling

```csharp
// SharpCoreDB supports multi-gigabyte rows via FILESTREAM
// Supports 3-tier storage:
// 1. Inline (<4KB) - stored in table
// 2. Overflow (4KB-256KB) - stored in overflow pages
// 3. FILESTREAM (>256KB) - stored in separate files

// Create table for large data
db.ExecuteSQL(@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        name TEXT,
        content BLOB           -- Can be many GB
    )
");

// Insert large data
var largeData = new byte[100_000_000];  // 100 MB
db.ExecuteSQL("INSERT INTO documents VALUES (1, 'large_doc', @content)",
    new SqlParameter("@content", largeData));

// Query returns data efficiently (no need to load all at once)
var result = db.ExecuteQuery("SELECT * FROM documents WHERE id = 1");
```

---

## Integration Patterns

### ASP.NET Core Integration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB
builder.Services.AddSharpCoreDB();

// Register your services
builder.Services.AddScoped<IUserRepository, UserRepository>();

var app = builder.Build();

// Middleware for database initialization
app.Use(async (context, next) =>
{
    var factory = context.RequestServices.GetRequiredService<DatabaseFactory>();
    using var db = factory.Create("./app.db", "password");
    
    // Ensure schema exists
    db.ExecuteSQL("CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT)");
    
    context.Items["Database"] = db;
    await next();
});

app.MapGet("/users", GetUsers);
app.Run();

// Endpoint example
async Task<IResult> GetUsers(IHttpContextAccessor httpContextAccessor)
{
    if (httpContextAccessor.HttpContext?.Items["Database"] is not Database db)
        return Results.BadRequest();
    
    var users = db.ExecuteQuery("SELECT * FROM users");
    return Results.Ok(users);
}
```

### Repository Pattern

```csharp
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
    Task<IEnumerable<User>> GetAllAsync();
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(int id);
}

public class UserRepository : IUserRepository
{
    private readonly DatabaseFactory _factory;
    
    public UserRepository(DatabaseFactory factory)
    {
        _factory = factory;
    }
    
    public async Task<User> GetByIdAsync(int id)
    {
        return await Task.Run(() =>
        {
            using var db = _factory.Create("./app.db", "password");
            var row = db.ExecuteQuery("SELECT * FROM users WHERE id = @id",
                new SqlParameter("@id", id)).FirstOrDefault();
            
            return row != null ? MapToUser(row) : null;
        });
    }
    
    public async Task<IEnumerable<User>> GetAllAsync()
    {
        return await Task.Run(() =>
        {
            using var db = _factory.Create("./app.db", "password");
            return db.ExecuteQuery("SELECT * FROM users")
                .Select(r => MapToUser(r))
                .ToList();
        });
    }
    
    public async Task AddAsync(User user)
    {
        await Task.Run(() =>
        {
            using var db = _factory.Create("./app.db", "password");
            db.ExecuteSQL(
                "INSERT INTO users (id, name, email, age) VALUES (@id, @name, @email, @age)",
                new SqlParameter("@id", user.Id),
                new SqlParameter("@name", user.Name),
                new SqlParameter("@email", user.Email),
                new SqlParameter("@age", user.Age)
            );
            db.Flush();
        });
    }
    
    // ... other methods
    
    private User MapToUser(dynamic row)
    {
        return new User 
        { 
            Id = row["id"], 
            Name = row["name"], 
            Email = row["email"], 
            Age = row["age"] 
        };
    }
}
```

### Unit Testing

```csharp
[TestClass]
public class UserRepositoryTests
{
    private DatabaseFactory _factory;
    private IUserRepository _repository;
    
    [TestInitialize]
    public void Setup()
    {
        _factory = new DatabaseFactory();
        _repository = new UserRepository(_factory);
    }
    
    [TestMethod]
    public async Task GetByIdAsync_WithValidId_ReturnsUser()
    {
        // Arrange
        using var db = _factory.CreateInMemory("TestDB");
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com', 30)");
        
        // Act
        var user = await _repository.GetByIdAsync(1);
        
        // Assert
        Assert.IsNotNull(user);
        Assert.AreEqual("Alice", user.Name);
    }
    
    [TestMethod]
    public async Task AddAsync_WithValidUser_InsertsUser()
    {
        // Arrange
        var newUser = new User { Id = 1, Name = "Bob", Email = "bob@example.com", Age = 25 };
        
        // Act
        await _repository.AddAsync(newUser);
        var retrieved = await _repository.GetByIdAsync(1);
        
        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("Bob", retrieved.Name);
    }
}
```

---

## Troubleshooting

### Common Issues

#### 1. Database Locked

**Problem**: "Database is locked" or "File is in use"

```csharp
// ❌ WRONG: Multiple database instances
var db1 = factory.Create("./db", "password");
var db2 = factory.Create("./db", "password");  // ❌ Conflict!

// ✅ CORRECT: One instance per application
using var db = factory.Create("./db", "password");
// All operations use this single instance
```

#### 2. Data Not Persisting

**Problem**: Data disappears after application restart

```csharp
// ❌ WRONG: Missing flush
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
// Data not saved!

// ✅ CORRECT: Always flush
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
db.Flush();      // Flush WAL to main file
db.ForceSave();  // Force checkpoint (optional but recommended)
```

#### 3. Out of Memory with Large Queries

**Problem**: Application runs out of memory

```csharp
// ❌ WRONG: Loading entire dataset
var allUsers = db.ExecuteQuery("SELECT * FROM users WHERE...").ToList();

// ✅ CORRECT: Use pagination
var pageSize = 100;
var page1 = db.ExecuteQuery("SELECT * FROM users LIMIT @limit OFFSET @offset",
    new SqlParameter("@limit", pageSize),
    new SqlParameter("@offset", 0)).ToList();
```

#### 4. Slow Queries

**Problem**: Queries are very slow

```csharp
// ✅ DO: Add indexes
db.ExecuteSQL("CREATE INDEX idx_email ON users(email) USING BTREE");

// ✅ DO: Use WHERE clauses with indexed columns
db.ExecuteQuery("SELECT * FROM users WHERE email = 'test@example.com'");

// ❌ DON'T: Full table scans
db.ExecuteQuery("SELECT * FROM users WHERE UPPER(name) LIKE 'ALICE%'");  // No index
```

#### 5. File Corruption

**Problem**: Database file is corrupted

```csharp
// SharpCoreDB has automatic recovery via WAL
// If corruption occurs:

// 1. Delete the .wal file (write-ahead log)
File.Delete("./app.db.wal");

// 2. Reopen database (will rebuild from main file)
using var db = factory.Create("./app.db", "password");

// 3. For SCDB storage mode, use repair tool
// See docs/scdb/PRODUCTION_GUIDE.md for repair procedures
```

### Debug Logging

```csharp
// Enable debug output
var options = new DatabaseOptions
{
    LogLevel = LogLevel.Debug,  // Verbose logging
};

using var db = factory.Create("./app.db", "password", options);

// All operations will be logged to console/debug output
db.ExecuteSQL("INSERT INTO users VALUES (...)");  // Logs to debug
```

---

## Best Practices

### 1. Always Use 'using' for Database Connections

```csharp
// ✅ CORRECT
using var db = factory.Create("./db", "password");
db.ExecuteSQL("...");
// ✅ Automatically disposed

// ❌ WRONG
var db = factory.Create("./db", "password");
db.ExecuteSQL("...");
// ❌ Not disposed, resources leak
```

### 2. Use Parameters to Prevent SQL Injection

```csharp
// ✅ SAFE
db.ExecuteSQL("SELECT * FROM users WHERE email = @email",
    new SqlParameter("@email", userInput));

// ❌ DANGEROUS
db.ExecuteSQL($"SELECT * FROM users WHERE email = '{userInput}'");  // SQL injection!
```

### 3. Use Batch Operations for Multiple Changes

```csharp
// ✅ FAST (44% faster)
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO users VALUES ({i}, ...)");
}
db.ExecuteBatchSQL(statements);

// ❌ SLOW
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO users VALUES ({i}, ...)");  // 1000 separate calls
}
```

### 4. Create Appropriate Indexes

```csharp
// ✅ Index columns used in WHERE clauses
db.ExecuteSQL("CREATE INDEX idx_email ON users(email) USING BTREE");

// ✅ Index columns used in ORDER BY
db.ExecuteSQL("CREATE INDEX idx_created ON users(created_at) USING BTREE");

// ❌ Don't over-index (slows down inserts)
// Typically 3-5 indexes per table is optimal
```

### 5. Always Flush After Writes

```csharp
// ✅ CORRECT
db.ExecuteSQL("INSERT INTO users VALUES (...)");
db.Flush();      // Ensure written to disk

// ❌ RISKY
db.ExecuteSQL("INSERT INTO users VALUES (...)");
// What if app crashes?
```

### 6. Use Transactions for Complex Operations

```csharp
// Begin transaction
db.ExecuteSQL("BEGIN TRANSACTION");

try
{
    // All operations in transaction
    db.ExecuteSQL("INSERT INTO accounts SET balance = balance - 100 WHERE id = 1");
    db.ExecuteSQL("INSERT INTO accounts SET balance = balance + 100 WHERE id = 2");
    
    // Commit if successful
    db.ExecuteSQL("COMMIT");
    db.Flush();
}
catch (Exception ex)
{
    // Rollback on error
    db.ExecuteSQL("ROLLBACK");
    throw;
}
```

### 7. Monitor Database Size

```csharp
// Check file size
var fileInfo = new FileInfo("./app.db");
var sizeMB = fileInfo.Length / (1024 * 1024);
Console.WriteLine($"Database size: {sizeMB} MB");

// For large databases, consider:
// - Archival of old data
// - Partitioning by time
// - Downsampling time-series data

// Compact database
db.ExecuteSQL("VACUUM");  // Reclaims unused space
db.Flush();
```

### 8. Plan for Growth

```csharp
// Initial capacity
var options = new DatabaseOptions
{
    PageSize = 4096,           // Standard page size
    CacheSize = 256,           // 1 MB cache (256 pages)
};

// For larger databases (>1 GB):
var options = new DatabaseOptions
{
    PageSize = 8192,           // Larger pages for sequential I/O
    CacheSize = 1024,          // 8 MB cache
};

using var db = factory.Create("./app.db", "password", options);
```

### 9. Backup Strategy

```csharp
// Simple backup: copy the database file
public void BackupDatabase(string sourceDb, string backupPath)
{
    // Close database first
    File.Copy(sourceDb, backupPath, overwrite: true);
    File.Copy($"{sourceDb}.wal", $"{backupPath}.wal", overwrite: true);
}

// Restore from backup
public void RestoreDatabase(string backupPath, string restoreDb)
{
    File.Copy(backupPath, restoreDb, overwrite: true);
    File.Copy($"{backupPath}.wal", $"{restoreDb}.wal", overwrite: true);
}
```

### 10. Performance Testing

```csharp
// Simple benchmark
var sw = System.Diagnostics.Stopwatch.StartNew();

for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO users VALUES ({i}, ...)");
}

sw.Stop();
Console.WriteLine($"1000 inserts: {sw.ElapsedMilliseconds}ms");

// Expected performance:
// - Individual inserts: ~5-10ms each (total 5-10 seconds for 1000)
// - Batch insert: 50-100ms for 1000 (100x faster!)
```

---

## Performance Benchmarks

SharpCoreDB significantly outperforms other embedded databases:

| Operation | SharpCoreDB | SQLite | LiteDB | Improvement |
|-----------|-------------|--------|--------|------------|
| INSERT 1000 rows | 3.68ms | 5.70ms | 6.51ms | 43-44% faster |
| SELECT full scan 10K rows | 0.5ms | 1.2ms | 1.15ms | 2.3x faster |
| UPDATE 1000 rows | 60ms | 325ms | 65ms | 5.4x faster |
| Aggregation (COUNT SUM AVG) | 1.08µs | 737µs | 30.9ms | 682-28,660x faster |

These benchmarks are with **encryption enabled** — SharpCoreDB actually has **0% overhead** for AES-256-GCM encryption, sometimes running **faster** than unencrypted SQLite!

---

## Resources

- **Project Repository**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **NuGet Package**: https://www.nuget.org/packages/SharpCoreDB
- **Architecture Guide**: [docs/SHARPCOREDB_EMBEDDED_DISTRIBUTED_GUIDE.md](docs/SHARPCOREDB_EMBEDDED_DISTRIBUTED_GUIDE.md)
- **SCDB Implementation**: [docs/scdb/IMPLEMENTATION_STATUS.md](docs/scdb/IMPLEMENTATION_STATUS.md)
- **Migration from SQLite**: [docs/migration/MIGRATION_GUIDE.md](docs/migration/MIGRATION_GUIDE.md)

---

## Support & Feedback

- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Sponsor**: [GitHub Sponsors](https://github.com/sponsors/mpcoredeveloper)

---

**Happy database development! 🚀**

Last updated: February 2026  
Version: 1.0.0
