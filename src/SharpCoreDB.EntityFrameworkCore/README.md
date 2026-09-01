# SharpCoreDB.EntityFrameworkCore

Entity Framework Core 10 provider for `SharpCoreDB`.

**Version:** `v1.9.5`
**Target framework:** `.NET 10`  
**Status:** Production-ready provider package


## Patch updates in v1.9.5

- ✅ Fixed EF Core materialization for aliased and quoted SELECT columns by normalizing DataReader column names and fallback value resolution.
- ✅ Added targeted regression tests for aliased and qualified column lookup behavior.
- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Root cause: Raw `Guid` objects were not normalized to strings before being sent to the engine during INSERT (only `DateTime` had this protection).
- Fix: Added `Guid` → canonical string normalization (`ToString("D")`) in `BuildParameterDictionary()` (both EF and raw ADO.NET command layers).
- The recommended pattern now works reliably with `Guid` primary keys and foreign keys:
  ```csharp
  dbContext.Companies
      .Include(x => x.Vacancies)
      .Where(x => x.Vacancies.Any(v => v.IsActive))
      .ToListAsync();
  ```
- Added regression test covering the ideal `Include` + navigation filter pattern with Guid keys.

## What this package covers

`SharpCoreDB.EntityFrameworkCore` gives EF Core applications a maintained provider entry point for SharpCoreDB with:

- `UseSharpCoreDB(...)` provider registration
- Relational provider services and type mappings
- Migration and update SQL generation support
- Raw SQL query/command workflows over SharpCoreDB storage
- Connection-string driven configuration for embedded encrypted databases

## Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 2.0.0.0
```

## DateTime Handling (Reliable Pattern)

`SharpCoreDB.EntityFrameworkCore` handles `DateTime` / `DateTimeOffset` using the same proven approach as the official SQLite provider and LiteDB:

- Automatic `ValueConverter` that stores values as **ISO-8601 round-trip strings** (`"o"` format)
- Column type forced to `TEXT`
- Global registration via `SharpCoreDBModelCustomizer` + per-entity fallback

**Recommended pattern for DateTime filters:**

```csharp
// Reliable way to filter by DateTime (avoids early NRE in EF Core's expression visitor)
var cutoff = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);

var results = dbContext.Entities
    .AsEnumerable()                    // Client-side evaluation for DateTime comparisons
    .Where(e => e.CreatedAt > cutoff)
    .ToList();
```

This pattern is stable and matches real-world usage with SQLite and LiteDB when using custom EF Core providers.

## Relationship Queries – Avoid Client-Side Filtering

**❌ Anti-pattern (common mistake):**

```csharp
var companies = await dbContext.Companies
    .Include(x => x.Vacancies)
    .AsNoTracking()
    .OrderBy(x => x.Name)
    .ToListAsync(cancellationToken);

return companies
    .Where(static x => x.Vacancies.Any(v => v.IsActive))   // Client-side!
    .ToList();
```

**✅ Correct (server-side – EF Core emits EXISTS):**

```csharp
return await dbContext.Companies
    .Include(x => x.Vacancies.Where(v => v.IsActive))
    .AsNoTracking()
    .Where(x => x.Vacancies.Any(v => v.IsActive))
    .OrderBy(x => x.Name)
    .ToListAsync(cancellationToken);
```

Always filter parents on the server when possible. The example in `Examples/AspNetCoreExample.cs` demonstrates the correct approach using `Product.IsActive`.

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
- `../../docs/FEATURE_MATRIX.md` - Package ecosystem coverage

## Notes

- Keep this README and `USAGE.md` as the maintained EF Core documentation pair.
- Older duplicate package guides should be removed instead of updated in parallel.

## Debugging Guid Foreign Key Issues (Recommended Diagnostic)

When using `Guid` primary keys with relationships (`Include` + navigation filters), you may encounter cases where the foreign keys appear not to be written correctly.

**Recommended diagnostic** (works 100% through the EF Core provider, no raw storage access):

```csharp
public static class SharpCoreDbDiagnostics
{
    /// <summary>
    /// Call this right after SaveChangesAsync() to verify Guid FKs were persisted.
    /// </summary>
    public static void DumpGuidForeignKeys<TContext, TChild>(
        TContext context, string label = "Guid FK Diagnostic")
        where TContext : DbContext
        where TChild : class
    {
        Console.WriteLine($"\n=== {label} ===");
        var children = context.Set<TChild>().AsNoTracking().ToList();

        foreach (var child in children)
        {
            var companyId = child.GetType().GetProperty("CompanyId")?.GetValue(child);
            var title = child.GetType().GetProperty("Title")?.GetValue(child) 
                     ?? child.GetType().GetProperty("Name")?.GetValue(child);

            Console.WriteLine($"  Title: \"{title}\", CompanyId: {companyId}");
        }
        Console.WriteLine("=== End ===\n");
    }
}

// Usage (right after SaveChangesAsync):
// SharpCoreDbDiagnostics.DumpGuidForeignKeys<YourDbContext, YourChildEntity>(dbContext);
```

This pattern helped identify that Guid foreign keys were not being correctly established during insert in certain navigation scenarios.



