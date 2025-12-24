# SharpCoreDB.Extensions

Powerful extensions voor SharpCoreDB including **Dapper integration** en **ASP.NET Core health checks**.

## ?? Features

- ? **Dapper Integration** - Gebruik Dapper met SharpCoreDB
- ? **Health Checks** - ASP.NET Core health check support
- ? **Connection Pooling** - Optimized connection management
- ? **Encryption Support** - Full AES-256-GCM encryption
- ? **Cross-Platform** - Windows, Linux, macOS, Android, iOS, IoT
- ? **Platform Optimizations** - AVX2/NEON intrinsics

## ?? Installation

```bash
dotnet add package SharpCoreDB.Extensions
```

```xml
<PackageReference Include="SharpCoreDB.Extensions" Version="1.0.0" />
```

## ?? Quick Start

### Dapper Integration

```csharp
using SharpCoreDB.Extensions.Dapper;
using Dapper;

// Open connection
using var connection = new SharpCoreDBConnection("Data Source=app.db;Encryption=true;Password=SecurePass");
connection.Open();

// Query with Dapper
var products = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Price > @MinPrice",
    new { MinPrice = 100 });

// Insert with Dapper
var product = new Product { Name = "Laptop", Price = 999.99m };
await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price) VALUES (@Name, @Price)",
    product);

// Transactions
using var transaction = connection.BeginTransaction();
try
{
    await connection.ExecuteAsync("INSERT INTO ...", param, transaction);
    await connection.ExecuteAsync("UPDATE ...", param, transaction);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### ASP.NET Core Health Checks

```csharp
// Program.cs
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: "Data Source=app.db;Encryption=true;Password=SecurePass",
        name: "sharpcoredb",
        tags: new[] { "db", "sharpcoredb" });

app.MapHealthChecks("/health");
```

**Health Check Response:**
```json
{
  "status": "Healthy",
  "results": {
    "sharpcoredb": {
      "status": "Healthy",
      "description": "SharpCoreDB is healthy",
      "data": {
        "database": "app.db",
        "encrypted": true,
        "responseTime": "5ms"
      }
    }
  }
}
```

## ?? Complete Documentation

Voor uitgebreide documentatie en voorbeelden:

### ?? [Complete Usage Guide op GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/SharpCoreDB.Extensions/USAGE_GUIDE.md)

Inclusief:
- Dapper advanced scenarios
- Multiple database connections
- Bulk operations
- Stored procedures
- Health check configuratie
- Performance tips
- Best practices
- Complete code voorbeelden

## ?? Related Packages

| Package | Description |
|---------|-------------|
| [SharpCoreDB](https://www.nuget.org/packages/SharpCoreDB) | Core database engine |
| [SharpCoreDB.EntityFrameworkCore](https://www.nuget.org/packages/SharpCoreDB.EntityFrameworkCore) | EF Core provider |
| [SharpCoreDB.Extensions](https://www.nuget.org/packages/SharpCoreDB.Extensions) | This package |

## ?? Examples

### Dapper Multi-Mapping

```csharp
var orders = await connection.QueryAsync<Order, Customer, Order>(
    @"SELECT o.*, c.* 
      FROM Orders o 
      INNER JOIN Customers c ON o.CustomerId = c.Id",
    (order, customer) => 
    {
        order.Customer = customer;
        return order;
    },
    splitOn: "Id");
```

### Custom Health Checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: connectionString,
        healthQuery: "SELECT COUNT(*) FROM Products",
        name: "database-products",
        failureStatus: HealthStatus.Degraded,
        timeout: TimeSpan.FromSeconds(5));
```

### Connection String Builder

```csharp
var builder = new SharpCoreConnectionStringBuilder
{
    DataSource = "app.db",
    Encryption = true,
    Password = "SecurePass",
    Pooling = true,
    MaxPoolSize = 100,
    CommandTimeout = 30
};

using var connection = new SharpCoreDBConnection(builder.ToString());
```

## ?? Platform Support

Ondersteunt alle SharpCoreDB platforms:
- Windows (x64, ARM64)
- Linux (x64, ARM64)
- macOS (x64, ARM64 / Apple Silicon)
- Android (ARM64, x64)
- iOS (ARM64)
- IoT/Embedded (ARM32, ARM64)

## ?? Security

```csharp
// Encrypted connection
var connection = new SharpCoreDBConnection(
    "Data Source=secure.db;Encryption=true;Password=VerySecurePassword123!");

// Use environment variables
var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
var connString = $"Data Source=app.db;Encryption=true;Password={password}";
```

## ? Performance

- Connection pooling enabled by default
- Optimized for Dapper's micro-ORM approach
- Platform-specific optimizations (AVX2/NEON)
- Minimal overhead over direct SharpCoreDB usage

## ?? Support

- **Documentation**: [GitHub Wiki](https://github.com/MPCoreDeveloper/SharpCoreDB/wiki)
- **Complete Guide**: [USAGE_GUIDE.md](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/SharpCoreDB.Extensions/USAGE_GUIDE.md)
- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)

## ?? License

MIT License - See [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE)

---

**Made with ?? for the .NET community**
