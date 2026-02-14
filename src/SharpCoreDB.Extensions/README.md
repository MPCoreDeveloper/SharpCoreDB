<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Extensions v1.3.0

  **Dapper Integration · Health Checks · Repository Pattern · Bulk Operations · Performance Monitoring**

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![C#](https://img.shields.io/badge/C%23-14-blueviolet.svg)](https://learn.microsoft.com/dotnet/csharp/)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Extensions)

</div>

---

Official extensions for **SharpCoreDB** providing Dapper integration, ASP.NET Core health checks, repository pattern, bulk operations, and query performance monitoring. Built for .NET 10 with C# 14.

## Table of Contents

- [Installation](#installation)
- [Feature Overview](#feature-overview)
- [Quick Start](#quick-start)
- [Dapper Integration](#dapper-integration)
- [Repository Pattern](#repository-pattern)
- [Bulk Operations](#bulk-operations)
- [Health Checks](#health-checks)
- [Performance Monitoring](#performance-monitoring)
- [Pagination](#pagination)
- [Type Mapping](#type-mapping)
- [Platform Support](#platform-support)
- [API Reference](#api-reference)

---

## Installation

```bash
dotnet add package SharpCoreDB.Extensions
```

**Dependencies** (automatically resolved):

| Package | Version | Purpose |
|---------|---------|---------|
| SharpCoreDB | 1.3.0 | Core database engine |
| Dapper | 2.1.66 | Micro-ORM for typed queries |
| Microsoft.Extensions.Diagnostics.HealthChecks | 10.0.2 | ASP.NET Core health checks |

---

## Feature Overview

| Feature | Namespace | Description |
|---------|-----------|-------------|
| **Dapper Connection** | `SharpCoreDB.Extensions` | `DbConnection` adapter for Dapper |
| **Async Extensions** | `SharpCoreDB.Extensions` | `QueryAsync<T>`, `ExecuteAsync`, `QueryPagedAsync<T>` |
| **Repository Pattern** | `SharpCoreDB.Extensions` | `DapperRepository<TEntity, TKey>` with CRUD |
| **Bulk Operations** | `SharpCoreDB.Extensions` | `BulkInsert<T>`, `BulkUpdate<T>`, `BulkDelete<TKey>` |
| **Health Checks** | `SharpCoreDB.Extensions` | ASP.NET Core `IHealthCheck` integration |
| **Performance Monitoring** | `SharpCoreDB.Extensions` | `QueryWithMetrics<T>`, `GetPerformanceReport()` |
| **Mapping Extensions** | `SharpCoreDB.Extensions` | Multi-table JOINs, custom mapping, projections |
| **Type Mapping** | `SharpCoreDB.Extensions` | `DapperTypeMapper` for .NET ↔ DB type conversion |
| **Unit of Work** | `SharpCoreDB.Extensions` | `DapperUnitOfWork` for transaction management |

---

## Quick Start

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions;

// Create database
var factory = new DatabaseFactory(serviceProvider);
using var db = factory.Create("./myapp.scdb", "StrongPassword!");

// Create a table
db.ExecuteSQL("CREATE TABLE products (Id INTEGER PRIMARY KEY, Name TEXT, Price REAL)");
db.ExecuteSQL("INSERT INTO products VALUES (1, 'Widget', 19.99)");
db.Flush();

// Query with Dapper — strongly typed
using var connection = db.GetDapperConnection();
connection.Open();

var products = connection.Query<Product>("SELECT * FROM products WHERE Price > @MinPrice",
    new { MinPrice = 10.0 });

foreach (var p in products)
{
    Console.WriteLine($"{p.Name}: ${p.Price}");
}
```

---

## Dapper Integration

### Get a Dapper Connection

```csharp
// Extension method on IDatabase
using var connection = database.GetDapperConnection();
connection.Open();

// Use all standard Dapper methods
var users = connection.Query<User>("SELECT * FROM users");
var user = connection.QueryFirstOrDefault<User>(
    "SELECT * FROM users WHERE Id = @Id", new { Id = 1 });
var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM users");
```

### Async Extension Methods

```csharp
// Direct extensions on IDatabase — no need to manually open connections
var users = await database.QueryAsync<User>("SELECT * FROM users");

var user = await database.QueryFirstOrDefaultAsync<User>(
    "SELECT * FROM users WHERE Id = @Id", new { Id = 1 });

var affected = await database.ExecuteAsync(
    "UPDATE users SET Name = @Name WHERE Id = @Id",
    new { Name = "Alice", Id = 1 });

var total = await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users");
```

### Transactions

```csharp
using var connection = database.GetDapperConnection();
connection.Open();
using var transaction = connection.BeginTransaction();

try
{
    connection.Execute(
        "INSERT INTO orders (UserId, Total) VALUES (@UserId, @Total)",
        new { UserId = 1, Total = 99.99 }, transaction);

    connection.Execute(
        "UPDATE inventory SET Qty = Qty - 1 WHERE ProductId = @Pid",
        new { Pid = 42 }, transaction);

    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

---

## Repository Pattern

### Basic Usage

```csharp
// Create a repository
var repo = new DapperRepository<User, int>(database, "users", keyColumn: "Id");

// CRUD operations
repo.Insert(new User { Name = "Alice", Email = "alice@example.com" });
var user = repo.GetById(1);
var all = repo.GetAll();
repo.Update(user);
repo.Delete(1);
var count = repo.Count();

// Async variants
await repo.InsertAsync(user);
var found = await repo.GetByIdAsync(1);
await repo.DeleteAsync(1);
```

### Read-Only Repository

```csharp
// For query-only scenarios (no Insert/Update/Delete)
var readRepo = new ReadOnlyDapperRepository<Product, int>(database, "products");
var products = readRepo.GetAll();
var total = readRepo.Count();
```

### Unit of Work

```csharp
using var uow = new DapperUnitOfWork(database);
uow.BeginTransaction();

try
{
    var userRepo = uow.GetRepository<User, int>("users");
    var orderRepo = uow.GetRepository<Order, int>("orders");

    userRepo.Insert(new User { Name = "Bob" });
    orderRepo.Insert(new Order { UserId = 1, Total = 50.0 });

    uow.Commit();
}
catch
{
    uow.Rollback();
    throw;
}
```

---

## Bulk Operations

```csharp
// Bulk insert — batched for performance
var users = Enumerable.Range(1, 10_000)
    .Select(i => new User { Name = $"User{i}", Email = $"user{i}@test.com" });

int inserted = database.BulkInsert("users", users, batchSize: 1000);

// Async bulk insert with cancellation
int count = await database.BulkInsertAsync("users", users, batchSize: 500, cancellationToken);

// Bulk update
database.BulkUpdate("users", updatedUsers, keyProperty: "Id");

// Bulk delete
database.BulkDelete("users", new[] { 1, 2, 3 }, keyColumn: "Id");
```

---

## Health Checks

### Basic Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        database,
        name: "sharpcoredb",
        testQuery: "SELECT 1",
        tags: ["db", "ready"]);

var app = builder.Build();
app.MapHealthChecks("/health");
```

### Lightweight (Connection Only)

```csharp
// Best for high-frequency liveness probes
builder.Services.AddHealthChecks()
    .AddSharpCoreDBLightweight(database, name: "sharpcoredb-lite");
```

### Comprehensive (All Diagnostics)

```csharp
// Includes cache stats, performance metrics, table checks
builder.Services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(database, name: "sharpcoredb-full");
```

### Custom Configuration

```csharp
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(database, options =>
    {
        options.TestQuery = "SELECT COUNT(*) FROM users";
        options.DegradedThresholdMs = 500;
        options.UnhealthyThresholdMs = 2000;
        options.CheckQueryCache = true;
        options.CheckPerformanceMetrics = true;
        options.Timeout = TimeSpan.FromSeconds(5);
    });
```

### Health Check Response Example

```json
{
  "status": "Healthy",
  "results": {
    "sharpcoredb": {
      "status": "Healthy",
      "description": "SharpCoreDB is operational",
      "data": {
        "connection": "OK",
        "query_execution_ms": 2,
        "cache_hit_rate": "85.50%",
        "health_check_duration_ms": 5
      }
    }
  }
}
```

---

## Performance Monitoring

### Query with Metrics

```csharp
// Track execution time and memory usage
var result = database.QueryWithMetrics<User>("SELECT * FROM users");
Console.WriteLine($"Rows: {result.Metrics.RowCount}");
Console.WriteLine($"Time: {result.Metrics.ExecutionTime.TotalMilliseconds}ms");
Console.WriteLine($"Memory: {result.Metrics.MemoryUsed} bytes");

// Async variant
var asyncResult = await database.QueryWithMetricsAsync<User>(
    "SELECT * FROM users WHERE Active = @Active",
    new { Active = true },
    queryName: "ActiveUsers");
```

### Performance Report

```csharp
var report = DapperPerformanceExtensions.GetPerformanceReport();
Console.WriteLine($"Total queries: {report.TotalQueries}");
Console.WriteLine($"Avg time: {report.AverageExecutionTime.TotalMilliseconds}ms");
Console.WriteLine($"Slowest: {report.SlowestQuery?.QueryName}");
Console.WriteLine($"Total memory: {report.TotalMemoryUsed} bytes");

// Clear metrics
DapperPerformanceExtensions.ClearMetrics();
```

### Timeout Warnings

```csharp
var results = database.QueryWithTimeout<User>(
    "SELECT * FROM users",
    timeout: TimeSpan.FromSeconds(2),
    onTimeout: elapsed => Console.WriteLine($"⚠ Query took {elapsed.TotalSeconds}s"));
```

---

## Pagination

```csharp
var page = await database.QueryPagedAsync<User>(
    "SELECT * FROM users ORDER BY Name",
    pageNumber: 2,
    pageSize: 25);

Console.WriteLine($"Page {page.PageNumber}/{page.TotalPages}");
Console.WriteLine($"Total items: {page.TotalCount}");
Console.WriteLine($"Has next: {page.HasNextPage}");

foreach (var user in page.Items)
{
    Console.WriteLine(user.Name);
}
```

---

## Type Mapping

### Custom Column Mapping

```csharp
// Map DB columns to different C# property names
DapperMappingExtensions.CreateTypeMap<User>(new Dictionary<string, string>
{
    ["user_name"] = "Name",
    ["email_address"] = "Email",
    ["created_at"] = "CreatedDate"
});
```

### Multi-Table JOINs

```csharp
var orders = database.QueryMultiMapped<Order, User, OrderWithUser>(
    "SELECT o.*, u.* FROM orders o JOIN users u ON o.UserId = u.Id",
    (order, user) => new OrderWithUser { Order = order, User = user },
    splitOn: "Id");
```

### Custom Mapping Function

```csharp
var products = database.QueryWithMapping(
    "SELECT * FROM products",
    row => new ProductDto
    {
        Id = (int)row["Id"],
        DisplayName = $"{row["Name"]} (${row["Price"]})"
    });
```

---

## Platform Support

| Platform | Architecture | Status |
|----------|-------------|--------|
| Windows | x64, ARM64 | ✅ Full support |
| Linux | x64, ARM64 | ✅ Full support |
| macOS | x64 (Intel), ARM64 (Apple Silicon) | ✅ Full support |
| Android | ARM64 | ✅ Supported |
| iOS | ARM64 | ✅ Supported |
| IoT/Embedded | ARM | ✅ Supported |

---

## API Reference

### Extension Methods on `IDatabase`

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetDapperConnection()` | `IDbConnection` | Creates a Dapper-compatible connection |
| `QueryAsync<T>()` | `Task<IEnumerable<T>>` | Typed async query |
| `QueryFirstOrDefaultAsync<T>()` | `Task<T?>` | Single result async query |
| `ExecuteAsync()` | `Task<int>` | Async command execution |
| `ExecuteScalarAsync<T>()` | `Task<T?>` | Async scalar query |
| `QueryPagedAsync<T>()` | `Task<PagedResult<T>>` | Paginated async query |
| `QueryWithMetrics<T>()` | `QueryResult<T>` | Query with performance tracking |
| `QueryWithMetricsAsync<T>()` | `Task<QueryResult<T>>` | Async query with metrics |
| `BulkInsert<T>()` | `int` | Batch insert entities |
| `BulkInsertAsync<T>()` | `Task<int>` | Async batch insert |
| `BulkUpdate<T>()` | `int` | Batch update entities |
| `BulkDelete<TKey>()` | `int` | Batch delete by keys |
| `QueryWithMapping<T>()` | `IEnumerable<T>` | Query with custom mapping |
| `QueryMapped<T>()` | `IEnumerable<T>` | Auto-mapped query |
| `QueryMultiMapped<T1,T2,TResult>()` | `IEnumerable<TResult>` | Multi-table JOIN mapping |

### Health Check Builders

| Method | Description |
|--------|-------------|
| `AddSharpCoreDB()` | Standard health check |
| `AddSharpCoreDBLightweight()` | Connection-only (fast) |
| `AddSharpCoreDBComprehensive()` | All diagnostics (detailed) |

### Classes

| Class | Description |
|-------|-------------|
| `DapperRepository<TEntity, TKey>` | Full CRUD repository |
| `ReadOnlyDapperRepository<TEntity, TKey>` | Read-only repository |
| `DapperUnitOfWork` | Transaction management |
| `DapperPerformanceExtensions` | Performance monitoring |
| `DapperTypeMapper` | .NET ↔ DB type conversion |
| `PagedResult<T>` | Pagination result container |

---

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

**Built with ❤️ for .NET 10 and C# 14**

// Basic health check
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        dbPath: "./app_db",
        password: "StrongPassword!");


// Advanced health check with options
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        name: "primary_database",
        dbPath: "./primary_db",
        password: "SecurePass123!",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "db", "primary", "critical" },
        timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

// Map health checks with detailed response
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.Run();
```
### Custom Health Check Logic

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB.Extensions.HealthChecks;

// Create custom health check
var healthCheck = new SharpCoreDBHealthCheck(
    dbPath: "./app_db",
    password: "StrongPassword!");

// Execute health check manually
var context = new HealthCheckContext();
var result = await healthCheck.CheckHealthAsync(context);

Console.WriteLine($"Status: {result.Status}");
Console.WriteLine($"Description: {result.Description}");
if (result.Exception != null)
{
    Console.WriteLine($"Error: {result.Exception.Message}");
}
```

---

## :building_construction: Architecture

### Dapper Integration Components

1. **DapperConnectionExtensions**
   - Extension method: `GetDapperConnection()`
   - Creates ADO.NET compatible connection wrapper
   - Manages connection lifetime and disposal

2. **SharpCoreDBConnection**
   - Implements `IDbConnection` interface
   - Wraps SharpCoreDB database instance
   - Translates ADO.NET calls to SharpCoreDB operations

3. **SharpCoreDBCommand**
   - Implements `IDbCommand` interface
   - Executes SQL statements via SharpCoreDB
   - Handles parameters and result sets

4. **SharpCoreDBDataReader**
   - Implements `IDataReader` interface
   - Provides forward-only cursor over results
   - Efficient data access for Dapper mapping

### Health Check Components

1. **SharpCoreDBHealthCheck**
   - Implements `IHealthCheck` interface
   - Verifies database connectivity
   - Performs basic read/write operations
   - Returns detailed health status

2. **HealthCheckBuilderExtensions**
   - Extension method: `AddSharpCoreDB()
   - Registers health check in DI container
   - Configurable name, tags, timeout, failure status

---

## :wrench: Configuration

### Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();

// Register SharpCoreDB
services.AddSharpCoreDB();

// Register database instance
services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    return factory.Create("./app_db", "StrongPassword!");
});

var provider = services.BuildServiceProvider();
var db = provider.GetRequiredService<Database>();

// Use with Dapper
using var connection = db.GetDapperConnection();
```

### Connection String Format

SharpCoreDB.Extensions uses the database path and password directly:

```csharp
// Format
dbPath: "./app_db"           // Relative or absolute path
password: "StrongPassword!"  // AES-256-GCM encryption key

// Examples
var db1 = factory.Create("./local_db", "Pass123!");
var db2 = factory.Create("/var/lib/myapp/data", "SecureKey!");
var db3 = factory.Create(@"C:\AppData\database", "MyPassword!");
```

---

## :link: Integration Examples

### ASP.NET Core Web API

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    return factory.Create("./api_db", "ApiPassword123!");
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddSharpCoreDB("./api_db", "ApiPassword123!");

var app = builder.Build();

// API endpoints
app.MapGet("/api/products", async (Database db) =>
{
    using var connection = db.GetDapperConnection();
    var products = await connection.QueryAsync<Product>(
        "SELECT * FROM products");
    return Results.Ok(products);
});

app.MapPost("/api/products", async (Database db, Product product) =>
{
    using var connection = db.GetDapperConnection();
    await connection.ExecuteAsync(
        "INSERT INTO products (id, name, price) VALUES (@Id, @Name, @Price)",
        product);
    return Results.Created($"/api/products/{product.Id}", product);
});

app.MapHealthChecks("/health");

app.Run();

record Product(int Id, string Name, decimal Price);
```

### Console Application

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;

var factory = new DatabaseFactory();
using var db = factory.Create("./console_db", "ConsolePass!");

db.ExecuteSQL("CREATE TABLE IF NOT EXISTS logs (id INTEGER PRIMARY KEY, message TEXT, timestamp TEXT)");

using var connection = db.GetDapperConnection();

// Insert logs
var logs = new []
{
    new { Id = 1, Message = "Application started", Timestamp = DateTime.UtcNow.ToString("O") },
    new { Id = 2, Message = "Processing data", Timestamp = DateTime.UtcNow.ToString("O") }
};

await connection.ExecuteAsync(
    "INSERT INTO logs (id, message, timestamp) VALUES (@Id, @Message, @Timestamp)",
    logs);

// Query logs
var recentLogs = await connection.QueryAsync<Log>(
    "SELECT * FROM logs ORDER BY timestamp DESC LIMIT 10");

foreach (var log in recentLogs)
{
    Console.WriteLine($"[{log.Timestamp}] {log.Message}");
}

record Log(int Id, string Message, string Timestamp);
```

### Background Service with Health Monitoring

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;
using SharpCoreDB.Extensions.HealthChecks;

var builder = Host.CreateApplicationBuilder(args);

// Add SharpCoreDB
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    return factory.Create("./worker_db", "WorkerPassword!");
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddSharpCoreDB("./worker_db", "WorkerPassword!", tags: new[] { "database" });

// Add background service
builder.Services.AddHostedService<DataProcessorService>();

var host = builder.Build();
await host.RunAsync();

class DataProcessorService : BackgroundService
{
    private readonly Database _db;
    private readonly HealthCheckService _healthCheck;

    public DataProcessorService(Database db, HealthCheckService healthCheck)
    {
        _db = db;
        _healthCheck = healthCheck;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Check database health
            var report = await _healthCheck.CheckHealthAsync(stoppingToken);
            if (report.Status != HealthStatus.Healthy)
            {
                Console.WriteLine($"Database unhealthy: {report.Status}");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            // Process data using Dapper
            using var connection = _db.GetDapperConnection();
            var pendingItems = await connection.QueryAsync<WorkItem>(
                "SELECT * FROM work_queue WHERE status = @Status LIMIT 100",
                new { Status = "pending" });

            foreach (var item in pendingItems)
            {
                // Process item...
                await connection.ExecuteAsync(
                    "UPDATE work_queue SET status = @Status WHERE id = @Id",
                    new { Status = "completed", item.Id });
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

record WorkItem(int Id, string Data, string Status);
```

---

## :test_tube: Testing

### Unit Testing with Dapper

```csharp
using Xunit;
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;

public class ProductRepositoryTests
{
    [Fact]
    public async Task Should_Insert_And_Query_Products()
    {
        // Arrange
        var factory = new DatabaseFactory();
        using var db = factory.Create(":memory:", "TestPassword");
        db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL)");

        using var connection = db.GetDapperConnection();

        // Act
        await connection.ExecuteAsync(
            "INSERT INTO products (id, name, price) VALUES (@Id, @Name, @Price)",
            new { Id = 1, Name = "Test Product", Price = 19.99 });

        var product = await connection.QueryFirstOrDefaultAsync<Product>(
            "SELECT * FROM products WHERE id = @Id",
            new { Id = 1 });

        // Assert
        Assert.NotNull(product);
        Assert.Equal("Test Product", product.Name);
        Assert.Equal(19.99m, product.Price);
    }
}

record Product(int Id, string Name, decimal Price);
```

### Health Check Testing

```csharp
using Xunit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB.Extensions.HealthChecks;

public class HealthCheckTests
{
    [Fact]
    public async Task Should_Return_Healthy_Status()
    {
        // Arrange
        var healthCheck = new SharpCoreDBHealthCheck(":memory:", "TestPass");
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Description);
    }

    [Fact]
    public async Task Should_Return_Unhealthy_On_Invalid_Password()
    {
        // Arrange
        var healthCheck = new SharpCoreDBHealthCheck("./nonexistent_db", "WrongPassword");
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }
}
```

---

## :package: Platform Support

### Supported Runtime Identifiers

| Platform | Architecture | Runtime ID | Status |
|----------|--------------|------------|--------|
| Windows | x64 | win-x64 | :white_check_mark: Supported |
| Windows | ARM64 | win-arm64 | :white_check_mark: Supported |
| Linux | x64 | linux-x64 | :white_check_mark: Supported |
| Linux | ARM64 | linux-arm64 | :white_check_mark: Supported |
| macOS | x64 (Intel) | osx-x64 | :white_check_mark: Supported |
| macOS | ARM64 (Apple Silicon) | osx-arm64 | :white_check_mark: Supported |

### Platform-Specific Optimizations

- **Hardware AES**: Automatic use of AES-NI instructions on supported CPUs
- **SIMD Vectorization**: AVX-512, AVX2, SSE2 for analytics
- **Native Performance**: Platform-specific builds for optimal performance
- **Zero P/Invoke**: Pure .NET implementation, no native dependencies

---

## :handshake: Compatibility

### Framework Requirements

- **.NET**: 10.0 or higher
- **C#**: 14.0 language features
- **SharpCoreDB**: 1.0.0 or higher
- **Dapper**: 2.1.66 or higher
- **Microsoft.Extensions.Diagnostics.HealthChecks**: 10.0.1 or higher

### Tested Platforms

- Windows 11 (x64, ARM64)
- Windows Server 2022 (x64)
- Ubuntu 24.04 LTS (x64, ARM64)
- macOS 14 Sonoma (Intel, Apple Silicon)
- Android 14+ (ARM64)
- iOS 17+ (ARM64)

---

## :page_facing_up: API Reference

### Extension Methods

```csharp
namespace SharpCoreDB.Extensions.Dapper
{
    public static class DapperConnectionExtensions
    {
        public static IDbConnection GetDapperConnection(this Database database);
    }
}

namespace SharpCoreDB.Extensions.HealthChecks
{
    public static class HealthCheckBuilderExtensions
    {
        public static IHealthChecksBuilder AddSharpCoreDB(
            this IHealthChecksBuilder builder,
            string dbPath,
            string password,
            string? name = null,
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null,
            TimeSpan? timeout = null);
    }
}
```

### Classes

```csharp
namespace SharpCoreDB.Extensions.HealthChecks
{
    public class SharpCoreDBHealthCheck : IHealthCheck
    {
        public SharpCoreDBHealthCheck(string dbPath, string password);
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default);
    }
}
```

---

## :books: Additional Resources

- **Main Repository**: [github.com/MPCoreDeveloper/SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)
- **Core Package**: [SharpCoreDB on NuGet](https://www.nuget.org/packages/SharpCoreDB)
- **Dapper Documentation**: [github.com/DapperLib/Dapper](https://github.com/DapperLib/Dapper)
- **Health Checks**: [Microsoft Docs](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)

---

## :handshake: Contributing

Contributions welcome! Areas for enhancement:

1. Additional Dapper features (bulk operations, table-valued parameters)
2. More health check options (custom queries, performance metrics)
3. Integration examples (Blazor, MAUI, Unity)
4. Documentation improvements
5. Platform-specific optimizations

See [CONTRIBUTING.md](../CONTRIBUTING.md) in the main repository for guidelines.

---

## :page_facing_up: License

MIT License - see [LICENSE](../LICENSE) file for details.

---

## :information_source: Version History

### 1.0.0 (Initial Release)

**Features**:
- :white_check_mark: Dapper integration with full ADO.NET compatibility
- :white_check_mark: ASP.NET Core health checks
- :white_check_mark: Full async/await support
- :white_check_mark: Transaction support
- :white_check_mark: Multi-platform builds (6 runtime identifiers)
- :white_check_mark: Comprehensive documentation and examples

**Dependencies**:
- SharpCoreDB 1.0.0
- Dapper 2.1.66
- Microsoft.Extensions.Diagnostics.HealthChecks 10.0.1

---

<div align="center">

**Built with :heart: for the SharpCoreDB ecosystem**

[Report Bug](https://github.com/MPCoreDeveloper/SharpCoreDB/issues) · [Request Feature](https://github.com/MPCoreDeveloper/SharpCoreDB/issues) · [Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)

</div>
