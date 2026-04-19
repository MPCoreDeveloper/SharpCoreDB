# SharpCoreDB.EntityFrameworkCore - Usage Guide

**Version:** `v1.7.0`  
**Target:** `.NET 10` / `C# 14`

This guide focuses on the maintained EF Core provider workflow for SharpCoreDB.

## Install

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.7.0
dotnet add package Microsoft.EntityFrameworkCore.Design
```

## Configure a DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().UseCollation("NOCASE");
            entity.Property(p => p.Price).HasColumnType("DECIMAL");
            entity.HasIndex(p => p.Name);
        });
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;

var services = new ServiceCollection();
services.AddDbContext<CatalogDbContext>(options =>
    options.UseSharpCoreDB(
        "Data Source=./catalog.scdb;Password=StrongPassword!;Pooling=true",
        sharpCoreOptions =>
        {
            sharpCoreOptions.CommandTimeout(30);
            sharpCoreOptions.MaxBatchSize(128);
        }));

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
await using var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
await db.Database.EnsureCreatedAsync();
```

## Run commands and queries

```csharp
await db.Database.ExecuteSqlRawAsync(
    "INSERT INTO Products (Id, Name, Price) VALUES ({0}, {1}, {2})",
    1,
    "Laptop",
    999.99m);

var results = await db.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Price >= {0}", 500m)
    .ToListAsync();
```

## Provider guidance

### Connection string format

```text
Data Source=<path>;Password=<password>;Pooling=true
```

### Recommended practices

- Prefer async EF Core APIs such as `EnsureCreatedAsync`, `ExecuteSqlRawAsync`, and `ToListAsync`.
- Use parameterized SQL for raw command/query workflows.
- Keep encryption settings in the connection string or external configuration, not hard-coded secrets.
- Use `UseCollation(...)` when your model requires deterministic text behavior.
- Treat this package as the maintained EF Core entry point; keep provider-specific docs in this file and `README.md`.

## Related docs

- `README.md`
- `NuGet.README.md`
- `../../docs/INDEX.md`
- `../../docs/FEATURE_MATRIX_v1.7.0.md`
