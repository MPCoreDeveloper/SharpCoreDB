# SharpCoreDB.EntityFrameworkCore

Entity Framework Core 10 provider for `SharpCoreDB`.

**Version:** `v1.7.1`  
**Target framework:** `.NET 10`  
**Status:** Production-ready provider package


## Patch updates in v1.7.1

- ✅ Fixed EF Core materialization for aliased and quoted SELECT columns by normalizing DataReader column names and fallback value resolution.
- ✅ Added targeted regression tests for aliased and qualified column lookup behavior.
- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## What this package covers

`SharpCoreDB.EntityFrameworkCore` gives EF Core applications a maintained provider entry point for SharpCoreDB with:

- `UseSharpCoreDB(...)` provider registration
- Relational provider services and type mappings
- Migration and update SQL generation support
- Raw SQL query/command workflows over SharpCoreDB storage
- Connection-string driven configuration for embedded encrypted databases

## Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.7.1
```

## Quick start

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public sealed class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}

public sealed class AppDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB(
            "Data Source=./shop.scdb;Password=StrongPassword!;Pooling=true",
            sharpCoreOptions =>
            {
                sharpCoreOptions.CommandTimeout(30);
                sharpCoreOptions.MaxBatchSize(128);
            });
    }
}
```

```csharp
await using var db = new AppDbContext();
await db.Database.EnsureCreatedAsync();

await db.Database.ExecuteSqlRawAsync(
    "INSERT INTO Products (Id, Name, Price) VALUES ({0}, {1}, {2})",
    1,
    "Laptop",
    999.99m);

var expensiveProducts = await db.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Price >= {0}", 500m)
    .ToListAsync();
```

## Connection string

The provider expects the SharpCoreDB connection string format used by `UseSharpCoreDB(...)`:

```text
Data Source=<path>;Password=<password>;Pooling=true
```

Common settings:

- `Data Source` - database file or directory path
- `Password` - encryption password for secured databases
- `Pooling` - enables provider-side pooling behavior when supported

## Recommended docs

- `USAGE.md` - Maintained usage guide with end-to-end setup notes
- `NuGet.README.md` - Package summary for NuGet consumers
- `../../docs/INDEX.md` - Canonical documentation hub
- `../../docs/FEATURE_MATRIX_v1.7.1.md` - Package ecosystem coverage

## Notes

- Keep this README and `USAGE.md` as the maintained EF Core documentation pair.
- Older duplicate package guides should be removed instead of updated in parallel.

