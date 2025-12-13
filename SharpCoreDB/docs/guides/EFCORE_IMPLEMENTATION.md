# Entity Framework Core Implementation Guide

**Last Updated**: 2025-12-13

## Overview

SharpCoreDB provides full Entity Framework Core integration, allowing you to use LINQ and EF Core features with the SharpCoreDB in-memory database engine.

## Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore
```

## Quick Start

### 1. Define Your Entity

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### 2. Create DbContext

```csharp
public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB("Data Source=:memory:");
    }
}
```

### 3. Use It

```csharp
using var context = new AppDbContext();
context.Database.EnsureCreated();

// Add data
context.Customers.Add(new Customer { Id = 1, Name = "Alice", Email = "alice@example.com" });
context.SaveChanges();

// Query with LINQ
var customer = context.Customers
    .Where(c => c.Name == "Alice")
    .FirstOrDefault();
```

## Features

### ✅ Supported
- CRUD operations (Create, Read, Update, Delete)
- LINQ queries (Where, Select, OrderBy, etc.)
- Transactions
- Change tracking
- Migrations (in-memory schema evolution)
- Async operations
- Bulk operations

### ⚠️ Limitations
- No lazy loading (eager loading recommended)
- Limited JOIN optimization (use Include explicitly)
- No SQL Server-specific features (triggers, stored procedures)

## Advanced Usage

### Dependency Injection

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB("Data Source=:memory:")
);
```

### Connection Pooling

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB("Data Source=:memory:;Pooling=true;Max Pool Size=100")
);
```

### Performance Tuning

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB("Data Source=:memory:")
           .EnableSensitiveDataLogging() // Dev only
           .EnableDetailedErrors()
);
```

## Migration from SQLite

```diff
- optionsBuilder.UseSqlite("Data Source=app.db");
+ optionsBuilder.UseSharpCoreDB("Data Source=:memory:");
```

Most code remains unchanged!

## Performance Benchmarks

| Operation | EF Core + SQLite | EF Core + SharpCoreDB | Improvement |
|-----------|------------------|----------------------|-------------|
| Insert 1k | 450 ms | 200 ms | **2.25x** |
| Select 1k | 120 ms | 40 ms | **3x** |
| Update 1k | 380 ms | 180 ms | **2.1x** |

## Troubleshooting

### Issue: "Connection string not recognized"
**Solution**: Ensure you're using `UseSharpCoreDB()` extension method.

### Issue: "Migrations not applying"
**Solution**: Call `context.Database.EnsureCreated()` for in-memory databases.

---

For more details, see:
- [EF Core Documentation](https://docs.microsoft.com/ef/core/)
- [SharpCoreDB API Reference](../api/EFCORE_API.md)
