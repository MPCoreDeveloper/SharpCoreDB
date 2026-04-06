<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Extensions

  **Dapper Integration · Health Checks · Repository Pattern · Bulk Operations · Performance Monitoring · FluentMigrator**

  **Version:** 1.7.0  
  **Status:** Production Ready ✅

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![C#](https://img.shields.io/badge/C%23-14-blueviolet.svg)](https://learn.microsoft.com/dotnet/csharp/)
  [![NuGet](https://img.shields.io/badge/NuGet-1.7.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Extensions)

</div>

---

Official extensions for **SharpCoreDB** providing developer convenience features:

- ✅ **Dapper Integration** - Micro-ORM for typed queries
- ✅ **Health Checks** - ASP.NET Core integration
- ✅ **FluentMigrator Integration (Optional)** - Schema migration support with custom SharpCoreDB processor
- ✅ **Repository Pattern** - Generic repository abstraction
- ✅ **Bulk Operations** - Batch insert/update/delete optimizations
- ✅ **Performance Monitoring** - Query metrics and diagnostics
- ✅ **Pagination** - Skip/take helpers
- ✅ **Type Mapping** - Automatic type conversions

Built for .NET 10 with C# 14.

---

## Installation

```bash
dotnet add package SharpCoreDB.Extensions --version 1.7.0
```

**Dependencies** (automatically resolved):

| Package | Version | Purpose |
|---------|---------|---------|
| SharpCoreDB | 1.7.0 | Core database engine |
| Dapper | 2.1.66+ | Micro-ORM for typed queries |
| Microsoft.Extensions.Diagnostics | 10.0+ | Health checks |

---

## Quick Start

### FluentMigrator Schema Migrations (Optional)

```csharp
using FluentMigrator;
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;

builder.Services.AddSharpCoreDBFluentMigrator(runner =>
{
    runner.ScanIn(typeof(Program).Assembly).For.Migrations();
});

var app = builder.Build();

using var scope = app.Services.CreateScope();
var migrationRunner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
migrationRunner.MigrateUp();
```

#### Automatic startup migration (Program.cs + HostedService)

```csharp
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharpCoreDBFluentMigrator(runner =>
{
    runner.ScanIn(typeof(Program).Assembly).For.Migrations();
});

builder.Services.AddHostedService<SharpCoreDbMigrationHostedService>();

var app = builder.Build();
app.Run();

public sealed class SharpCoreDbMigrationHostedService(IServiceProvider serviceProvider) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

To run startup migrations against a remote SharpCoreDB server, replace registration with:

```csharp
builder.Services.AddSharpCoreDBFluentMigratorGrpc(
    "Server=localhost;Port=5001;Database=master;SSL=true;Username=admin;Password=secret",
    runner => runner.ScanIn(typeof(Program).Assembly).For.Migrations());
```

#### Remote Server Mode (gRPC)

```csharp
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;

builder.Services.AddSharpCoreDBFluentMigratorGrpc(
    "Server=localhost;Port=5001;Database=master;SSL=true;Username=admin;Password=secret",
    runner => runner.ScanIn(typeof(Program).Assembly).For.Migrations());

var app = builder.Build();

using var scope = app.Services.CreateScope();
var migrationRunner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
migrationRunner.MigrateUp();
```

Remote mode executes SQL over `SharpCoreDB.Client` and does not expose an `IDbConnection` for `PerformDBOperationExpression` callbacks.

Example migration:

```csharp
[Migration(2026032301, "Create users table")]
public sealed class CreateUsersTableMigration : Migration
{
    public override void Up()
    {
        Create.Table("users")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("created_utc").AsDateTime().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("users");
    }
}
```

The integration stores migration state in `__SharpMigrations` (`Version`, `AppliedOn`, `Description`) and supports `MigrateUp`, targeted `MigrateUp(version)`, and rollback operations.

### Dapper Integration

```csharp
using SharpCoreDB.Extensions;

var database = provider.GetRequiredService<IDatabase>();

// Query with Dapper
var users = await database.QueryAsync<User>(
    "SELECT * FROM users WHERE age > @minAge",
    new { minAge = 18 }
);

foreach (var user in users)
{
    Console.WriteLine($"{user.Name}: {user.Age}");
}
```

### Health Checks

```csharp
services.AddHealthChecks()
    .AddSharpCoreDBHealthCheck(dbPath, password: "secure!");
```

### Repository Pattern

```csharp
// Generic repository with CRUD operations
var repository = new Repository<User>(database, "users");

var user = await repository.GetByIdAsync(1);
await repository.AddAsync(new User { Name = "Alice", Age = 30 });
await repository.UpdateAsync(user);
await repository.DeleteAsync(1);
```

### Bulk Operations

```csharp
// Fast batch insert
var users = new List<User>
{
    new("Alice", 30),
    new("Bob", 25),
    new("Carol", 28)
};

await repository.BulkInsertAsync(users);
```

---

## Features

### Dapper Query Mapping

```csharp
// Type-safe queries with automatic mapping
var results = await database.QueryAsync<(int Id, string Name, int Age)>(
    "SELECT id, name, age FROM users WHERE department = @dept",
    new { dept = "Engineering" }
);
```

### Multiple Result Sets

```csharp
// Get multiple queries in one round-trip
var (users, departments) = await database.QueryMultipleAsync<User, Department>(
    @"SELECT * FROM users;
      SELECT * FROM departments;",
    mapAction: (users, departments) => (users.ToList(), departments.ToList())
);
```

### Health Check Integration

```csharp
var health = await database.HealthCheckAsync();

if (health.IsHealthy)
{
    Console.WriteLine("Database is operational");
}
else
{
    Console.WriteLine($"Health issue: {health.Details}");
}
```

### Repository CRUD

```csharp
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id);
    Task<IEnumerable<User>> GetAllAsync();
    Task<IEnumerable<User>> FindAsync(Expression<Func<User, bool>> predicate);
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(int id);
}

var userRepo = new Repository<User>(database, "users");
var allUsers = await userRepo.GetAllAsync();
```

### Pagination

```csharp
var page = await repository.GetPageAsync(pageNumber: 2, pageSize: 10);

Console.WriteLine($"Page {page.PageNumber} of {page.TotalPages}");
foreach (var item in page.Items)
{
    Console.WriteLine(item);
}
```

### Performance Monitoring

```csharp
// Enable query timing
database.EnablePerformanceMonitoring();

var users = await database.QueryAsync("SELECT * FROM users");

var metrics = database.GetPerformanceMetrics();
Console.WriteLine($"Query time: {metrics.LastQueryMs}ms");
Console.WriteLine($"Total queries: {metrics.TotalQueries}");
```

---

## Common Patterns

### Service Layer with Repository

```csharp
public class UserService
{
    private readonly IRepository<User> _userRepository;

    public UserService(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User> GetUserAsync(int id)
    {
        return await _userRepository.GetByIdAsync(id);
    }

    public async Task RegisterUserAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        await _userRepository.AddAsync(user);
    }

    public async Task<IEnumerable<User>> SearchAsync(string namePrefix)
    {
        return await _userRepository.FindAsync(
            u => u.Name.StartsWith(namePrefix)
        );
    }
}
```

### Bulk Import

```csharp
public async Task ImportUsersAsync(List<User> users)
{
    // Efficient batch operation
    var repository = new Repository<User>(database, "users");
    
    // Split into chunks to avoid memory issues
    const int batchSize = 1000;
    for (int i = 0; i < users.Count; i += batchSize)
    {
        var batch = users.Skip(i).Take(batchSize).ToList();
        await repository.BulkInsertAsync(batch);
    }
}
```

### Dependency Injection Setup

```csharp
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<UserService>();

// In UserRepository
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(IDatabase database) 
        : base(database, "users") { }

    public async Task<User> GetByNameAsync(string name)
    {
        return await QuerySingleAsync(
            "SELECT * FROM users WHERE name = ?",
            [name]
        );
    }
}
```

---

## API Reference

### Dapper Methods

| Method | Purpose |
|--------|---------|
| `QueryAsync<T>(sql, param?)` | Query typed results |
| `QuerySingleAsync<T>(sql, param?)` | Single result |
| `QueryFirstOrDefaultAsync<T>(sql, param?)` | First or null |
| `ExecuteAsync(sql, param?)` | Execute non-query |
| `QueryMultipleAsync(sql, param?)` | Multiple result sets |

### Repository Methods

| Method | Purpose |
|--------|---------|
| `GetByIdAsync(id)` | Get by primary key |
| `GetAllAsync()` | Get all items |
| `FindAsync(predicate)` | Filter items |
| `AddAsync(item)` | Insert |
| `UpdateAsync(item)` | Update |
| `DeleteAsync(id)` | Delete |
| `BulkInsertAsync(items)` | Batch insert |
| `GetPageAsync(page, size)` | Paginated results |

### Health Check Methods

| Method | Purpose |
|--------|---------|
| `HealthCheckAsync()` | Check database health |
| `CanConnectAsync()` | Test connection |
| `GetDatabaseInfoAsync()` | Get stats |

---

## Performance Tips

1. **Use Bulk Operations** - 10-50x faster than individual inserts
2. **Enable Pagination** - Don't load all data at once
3. **Monitor Performance** - Use `EnablePerformanceMonitoring()`
4. **Index Frequently Queried Columns** - Especially for large tables
5. **Use Prepared Statements** - Let Dapper handle parameterization

---

## See Also

- **[Core SharpCoreDB](../SharpCoreDB/README.md)** - Database engine
- **[Analytics](../SharpCoreDB.Analytics/README.md)** - Data analysis
- **[Vector Search](../SharpCoreDB.VectorSearch/README.md)** - Embeddings
- **[User Manual](../../docs/USER_MANUAL.md)** - Complete guide

---

## License

MIT License - See [LICENSE](../../LICENSE)

---

**Last Updated:** March 23, 2026 | Version 1.7.0
