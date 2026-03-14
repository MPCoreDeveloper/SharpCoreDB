# SharpCoreDB.EntityFrameworkCore v1.5.0

**Entity Framework Core Provider for SharpCoreDB**

Full EF Core integration with SharpCoreDB's encryption and performance for modern .NET applications.

## ✨ What's New in v1.5.0

- ✅ Inherits metadata improvements from SharpCoreDB v1.5.0
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
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.5.0
```

**Requires:** SharpCoreDB v1.5.0+, EntityFrameworkCore v8.0+

---

**Version:** 1.5.0 | **Status:** ✅ Production Ready

