<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB.Extensions
  
  **Dapper Integration & Health Checks for SharpCoreDB**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Extensions)
  
</div>

---

Official extensions for SharpCoreDB providing **Dapper integration** for streamlined data access and **ASP.NET Core health checks** for database monitoring. Built for .NET 10 with C# 14 and optimized for Windows, Linux, macOS, Android, iOS, and IoT/embedded devices.

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Dependencies**: SharpCoreDB 1.0.0, Dapper 2.1.66
- **Multi-Platform**: Windows (x64/ARM64), Linux (x64/ARM64), macOS (x64/ARM64)

---

## :rocket: Quickstart

### Installation

```bash
dotnet add package SharpCoreDB.Extensions
```

### Dapper Integration

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;

var factory = new DatabaseFactory();
using var db = factory.Create("./app_db", "StrongPassword!");

// Create table
db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price REAL)");

// Get Dapper connection
using var connection = db.GetDapperConnection();

// Use Dapper for powerful queries
var products = await connection.QueryAsync<Product>(
    "SELECT * FROM products WHERE price > @MinPrice",
    new { MinPrice = 10.0 });

// Execute commands with Dapper
await connection.ExecuteAsync(
    "INSERT INTO products VALUES (@Id, @Name, @Price)",
    new { Id = 1, Name = "Widget", Price = 19.99 });

// Transactions with Dapper
using var transaction = connection.BeginTransaction();
await connection.ExecuteAsync(
    "UPDATE products SET price = price * 1.1 WHERE id = @Id",
    new { Id = 1 }, transaction);
transaction.Commit();
```

### Health Checks

```csharp
using SharpCoreDB.Extensions.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add SharpCoreDB health check
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(
        name: "sharpcoredb",
        dbPath: "./app_db",
        password: "StrongPassword!",
        tags: new[] { "db", "sharpcoredb" });

var app = builder.Build();

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();
```

---

## :star: Key Features

### :zap: **Dapper Integration**

- **Familiar API**: Use Dapper's powerful fluent API with SharpCoreDB
- **Full Async Support**: All async methods supported (QueryAsync, ExecuteAsync, etc.)
- **Type Safety**: Strongly-typed queries with automatic mapping
- **Performance**: Combines SharpCoreDB's speed with Dapper's efficiency
- **Transactions**: Full transaction support with BeginTransaction()
- **Parameter Binding**: Automatic parameter mapping and SQL injection prevention

### :heart: **Health Checks**

- **ASP.NET Core Integration**: Native health check support
- **Configurable Checks**: Verify database connectivity and basic operations
- **Custom Tags**: Organize health checks with tags
- **Failure Details**: Detailed error information for diagnostics
- **Production Ready**: Built for monitoring and alerting systems

### :globe_with_meridians: **Multi-Platform Support**

- **Windows**: x64, ARM64 with hardware AES acceleration
- **Linux**: x64, ARM64 with SIMD optimizations
- **macOS**: x64 (Intel), ARM64 (Apple Silicon)
- **Mobile**: Android and iOS support
- **IoT**: Embedded devices and ARM platforms
- **Platform-Specific Builds**: Optimized assemblies for each runtime

---

## :book: Detailed Usage

### Dapper Connection API

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions.Dapper;

var factory = new DatabaseFactory();
using var db = factory.Create("./app_db", "SecurePassword123!");

// Get connection
using var connection = db.GetDapperConnection();

// Query operations
var users = await connection.QueryAsync<User>("SELECT * FROM users");
var user = await connection.QueryFirstOrDefaultAsync<User>(
    "SELECT * FROM users WHERE id = @Id", new { Id = 1 });

// Execute operations
await connection.ExecuteAsync(
    "INSERT INTO users (id, name, email) VALUES (@Id, @Name, @Email)",
    new { Id = 1, Name = "Alice", Email = "alice@example.com" });

// Batch operations
var updates = new[]
{
    new { Id = 1, Name = "Alice Updated" },
    new { Id = 2, Name = "Bob Updated" }
};
await connection.ExecuteAsync(
    "UPDATE users SET name = @Name WHERE id = @Id",
    updates);

// Scalar operations
var count = await connection.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM users WHERE age > @MinAge",
    new { MinAge = 18 });

// Multiple result sets
using var multi = await connection.QueryMultipleAsync(
    "SELECT * FROM users; SELECT * FROM products");
var allUsers = multi.Read<User>();
var allProducts = multi.Read<Product>();
```

### Transaction Support

```csharp
using var connection = db.GetDapperConnection();
using var transaction = connection.BeginTransaction();

try
{
    await connection.ExecuteAsync(
        "INSERT INTO orders (id, user_id, total) VALUES (@Id, @UserId, @Total)",
        new { Id = 1, UserId = 100, Total = 99.99 }, transaction);
    
    await connection.ExecuteAsync(
        "UPDATE inventory SET quantity = quantity - @Qty WHERE product_id = @ProductId",
        new { Qty = 1, ProductId = 42 }, transaction);
    
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### Health Check Configuration

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpCoreDB.Extensions.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

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
   - Extension method: `AddSharpCoreDB()`
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
var logs = new[]
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
