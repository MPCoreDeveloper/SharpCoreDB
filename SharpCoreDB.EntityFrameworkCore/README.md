# SharpCoreDB.EntityFrameworkCore

**Entity Framework Core provider for SharpCoreDB encrypted database engine**

[![.NET](https://img.shields.io/badge/.NET-10-blue)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-green)](https://docs.microsoft.com/dotnet/csharp/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Seamlessly integrate SharpCoreDB with Entity Framework Core for encrypted, file-based database operations with modern C# 14 patterns.

## ✨ Features

- ✅ **Full Entity Framework Core Support** - Use familiar EF Core APIs with SharpCoreDB
- 🔒 **Built-in AES-256-GCM Encryption** - All data encrypted at rest automatically
- 🚀 **Modern C# 14 Patterns** - Primary constructors, collection expressions, pattern matching
- ⚡ **High Performance** - Optimized for concurrent workloads
- 📦 **Pure .NET Implementation** - No native dependencies, runs anywhere .NET 10 runs
- 🔄 **Connection Pooling** - Efficient database connection management
- 🎯 **LINQ Support** - Write type-safe queries with full LINQ support
- 📊 **Relationship Mapping** - One-to-many, many-to-many, complex relationships
- 💾 **File-Based Storage** - Simple deployment, easy backups
- 🧪 **Test-Friendly** - Easy integration with in-memory or real database testing

## 📦 Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore
```

Or add to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="SharpCoreDB.EntityFrameworkCore" Version="1.0.0" />
</ItemGroup>
```

## 🚀 Quick Start

### 1. Define Your Entities

```csharp
using System.ComponentModel.DataAnnotations;

public class Product
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public decimal Price { get; set; }
    
    public bool IsActive { get; set; }
}
```

### 2. Create Your DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

// Modern C# 14 primary constructor
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
        });
    }
}
```

### 3. Configure Services

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;

var services = new ServiceCollection();

services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB(
        "Data Source=./myapp.db;Password=MySecurePassword123;Cache=Shared"));

var serviceProvider = services.BuildServiceProvider();
```

### 4. Use Your DbContext

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Create
var product = new Product
{
    Name = "Laptop",
    Price = 999.99m,
    IsActive = true
};

context.Products.Add(product);
await context.SaveChangesAsync();

// Read
var products = await context.Products
    .Where(p => p.IsActive)
    .OrderBy(p => p.Name)
    .ToListAsync();

// Update
var productToUpdate = await context.Products.FindAsync(1);
if (productToUpdate is not null)
{
    productToUpdate.Price = 899.99m;
    await context.SaveChangesAsync();
}

// Delete
var productToDelete = await context.Products.FindAsync(1);
if (productToDelete is not null)
{
    context.Products.Remove(productToDelete);
    await context.SaveChangesAsync();
}
```

## 🔗 Connection String Format

```
Data Source=<path>;Password=<password>[;Cache=Shared][;ReadOnly=true]
```

**Parameters:**
- `Data Source` (required): Path to database directory
- `Password` (required): AES-256-GCM encryption password
- `Cache` (optional): `Shared` enables connection pooling
- `ReadOnly` (optional): `true` for read-only access

**Examples:**

```csharp
// Basic (encrypted, no pooling)
"Data Source=./mydb.db;Password=MyPassword"

// With pooling (recommended for web apps)
"Data Source=./mydb.db;Password=MyPassword;Cache=Shared"

// Read-only mode
"Data Source=./mydb.db;Password=MyPassword;ReadOnly=true"
```

## 🌐 ASP.NET Core Integration

```csharp
// Program.cs
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add SharpCoreDB with EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB(
        builder.Configuration.GetConnectionString("SharpCoreDB")!));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.EnsureCreatedAsync();
}

app.MapGet("/products", async (AppDbContext db) =>
    await db.Products.ToListAsync());

app.Run();
```

## 🎯 Modern C# 14 Features

This provider leverages the latest C# 14 features:

```csharp
// ✅ Primary constructors
public class MyDbContext(DbContextOptions<MyDbContext> options) : DbContext(options)
{
    // ...
}

// ✅ Collection expressions
var products = new List<Product>
{
    new() { Name = "Laptop", Price = 999m },
    new() { Name = "Mouse", Price = 29m }
};
// or
var products = [
    new Product { Name = "Laptop", Price = 999m },
    new Product { Name = "Mouse", Price = 29m }
];

// ✅ Pattern matching
if (product is not null) // 'not' pattern
{
    product.Price = 899m;
}

// ✅ ArgumentNullException.ThrowIfNull
ArgumentNullException.ThrowIfNull(context);

// ✅ Expression-bodied members
public DbSet<Product> Products => Set<Product>();
```

## 📚 Documentation

- **[Complete Usage Guide](USAGE.md)** - Comprehensive documentation with examples
- **[Examples](Examples/)** - Complete working examples
  - `CompleteExample.cs` - Full CRUD, queries, relationships, transactions
  - `AspNetCoreExample.cs` - ASP.NET Core web API example

## 🔒 Security Best Practices

### 1. Secure Password Management

```csharp
// ❌ BAD: Hardcoded password
options.UseSharpCoreDB("Data Source=./db;Password=MyPassword123");

// ✅ GOOD: Use environment variables
var password = Environment.GetEnvironmentVariable("DB_PASSWORD") 
               ?? throw new InvalidOperationException("Password not set");
options.UseSharpCoreDB($"Data Source=./db;Password={password}");

// ✅ BETTER: Use configuration
var connectionString = builder.Configuration.GetConnectionString("SharpCoreDB");
options.UseSharpCoreDB(connectionString);
```

### 2. appsettings.json Configuration

```json
{
  "ConnectionStrings": {
    "SharpCoreDB": "Data Source=./myapp.db;Password=<use-secrets>;Cache=Shared"
  }
}
```

Use [.NET Secret Manager](https://docs.microsoft.com/aspnet/core/security/app-secrets) or [Azure Key Vault](https://docs.microsoft.com/azure/key-vault/) for production.

## ⚡ Performance Tips

1. **Enable Connection Pooling**
   ```csharp
   "Data Source=./db;Password=pass;Cache=Shared"
   ```

2. **Use AsNoTracking for Read-Only Queries**
   ```csharp
   var products = await context.Products
       .AsNoTracking()
       .ToListAsync();
   ```

3. **Batch Inserts**
   ```csharp
   context.Products.AddRange(products);
   await context.SaveChangesAsync();
   ```

4. **Use Projections**
   ```csharp
   var names = await context.Products
       .Select(p => new { p.Id, p.Name })
       .ToListAsync();
   ```

## 🧪 Testing

### Unit Tests (In-Memory)

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(databaseName: "TestDb")
    .Options;

using var context = new AppDbContext(options);
// Test your code...
```

### Integration Tests (Real Database)

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSharpCoreDB($"Data Source=./test_{Guid.NewGuid()};Password=TestPass")
    .Options;

using var context = new AppDbContext(options);
await context.Database.EnsureCreatedAsync();
// Test with real SharpCoreDB...
```

## 📊 Supported Data Types

| .NET Type | SharpCoreDB Type |
|-----------|------------------|
| `int` | `INTEGER` |
| `long` | `LONG` |
| `string` | `TEXT` |
| `bool` | `BOOLEAN` |
| `DateTime` | `DATETIME` |
| `decimal` | `DECIMAL` |
| `double` | `REAL` |
| `Guid` | `GUID` |
| `byte[]` | `BLOB` |

## 🆚 Comparison with Other Providers

| Feature | SharpCoreDB.EF | SQLite.EF | SQL Server.EF |
|---------|----------------|-----------|---------------|
| **Built-in Encryption** | ✅ AES-256-GCM | ❌ | ✅ (Enterprise) |
| **Pure .NET** | ✅ | ❌ | ❌ |
| **File-based** | ✅ | ✅ | ❌ |
| **No Native Dependencies** | ✅ | ❌ | ❌ |
| **Modern C# 14** | ✅ | ⚠️ | ⚠️ |
| **Cross-platform** | ✅ | ✅ | ✅ |

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](../LICENSE) file for details.

## 🔗 Related Projects

- **[SharpCoreDB](../README.md)** - Core database engine
- **[SharpCoreDB.Extensions](../SharpCoreDB.Extensions/README.md)** - Dapper integration & extensions
- **[SharpCoreDB.Benchmarks](../SharpCoreDB.Benchmarks/README.md)** - Performance benchmarks

## 🙏 Acknowledgments

Built with ❤️ by [MPCoreDeveloper](https://github.com/MPCoreDeveloper) & GitHub Copilot

Powered by:
- [Entity Framework Core 10.0](https://docs.microsoft.com/ef/core/)
- [SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)
- [.NET 10](https://dotnet.microsoft.com/)

---

**Questions?** Check out the [Usage Guide](USAGE.md) or [open an issue](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)!
