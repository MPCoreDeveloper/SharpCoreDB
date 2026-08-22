# SharpCoreDB.EntityFrameworkCore v1.9.1

**Entity Framework Core Provider for SharpCoreDB**

Full EF Core integration with SharpCoreDB's encryption and performance for modern .NET applications.


## Patch updates in v1.9.1

- ✅ Fixed critical Guid foreign key persistence bug when using `Include` + navigation filters (e.g. `Where(x => x.Children.Any(...))`).
- Root cause was missing Guid normalization during INSERT parameter binding (now aligned with DateTime handling).
- The recommended pattern now works reliably with Guid primary keys and foreign keys.

## Patch updates in v1.9.0

- ✅ Fixed EF Core materialization for aliased and quoted SELECT columns by normalizing DataReader column names and fallback value resolution.
- ✅ Added targeted regression tests for aliased and qualified column lookup behavior.
- ✅ Aligned package metadata and version references to the synchronized 1.9.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## ✨ What's New in v1.9.0

- ✅ Inherits metadata improvements from SharpCoreDB v1.9.0
- ✅ Entity Framework Core integration
- ✅ Enterprise distributed features support
- ✅ Zero breaking changes
- ✅ Production ready

## 🚀 Key Features

- **Full EF Core Support**: LINQ queries, migrations, relationships
- **Encryption**: Transparent AES-256-GCM encryption
- **Performance**: High-speed data access with built-in caching
- **MVCC**: Multi-version concurrency control
- **Transactions**: ACID guarantees across operations

## 💻 Quick Example

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSharpCoreDB("mydb.scdb", "password");
    }
}

using var context = new AppDbContext();
var users = await context.Users.Where(u => u.IsActive).ToListAsync();
```

## 📚 Documentation

- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)
- [Entity Framework Integration](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/architecture/README.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.9.1
```

**Requires:** SharpCoreDB v1.9.4+, EntityFrameworkCore v8.0+

---

**Version:** 1.9.1 | **Status:** ✅ Production Ready (Guid FK fix included)




