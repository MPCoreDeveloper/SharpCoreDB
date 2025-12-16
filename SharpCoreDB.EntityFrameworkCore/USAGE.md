# SharpCoreDB.EntityFrameworkCore - Usage Guide

**Version**: 1.0.0  
**Target**: .NET 10 with C# 14  
**License**: MIT  
**Author**: MPCoreDeveloper & GitHub Copilot

---

## ?? Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore
```

Or add to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="SharpCoreDB.EntityFrameworkCore" Version="1.0.0" />
</ItemGroup>
```

---

## ?? Quick Start

### 1. Define Your Entity Models

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyApp.Models;

// Simple entity with auto-generated ID
public class Product
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Column(TypeName = "DECIMAL")]
    public decimal Price { get; set; }
    
    public bool IsActive { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Entity with relationships
public class Order
{
    [Key]
    public int OrderId { get; set; }
    
    public int CustomerId { get; set; }
    
    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; set; }
    
    public DateTime OrderDate { get; set; }
    
    [Column(TypeName = "DECIMAL")]
    public decimal TotalAmount { get; set; }
    
    public ICollection<OrderItem> Items { get; set; } = [];
}

public class Customer
{
    [Key]
    public int CustomerId { get; set; }
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string Email { get; set; } = string.Empty;
    
    public ICollection<Order> Orders { get; set; } = [];
}

public class OrderItem
{
    [Key]
    public int OrderItemId { get; set; }
    
    public int OrderId { get; set; }
    
    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
    
    public int ProductId { get; set; }
    
    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }
    
    public int Quantity { get; set; }
    
    [Column(TypeName = "DECIMAL")]
    public decimal UnitPrice { get; set; }
}
```

### 2. Create Your DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Models;

namespace MyApp.Data;

/// <summary>
/// Application database context using SharpCoreDB.
/// Modern C# 14 with primary constructors.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Product
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("DECIMAL");
            entity.HasIndex(e => e.Name); // Create index on Name
        });

        // Configure Order
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(e => e.OrderId);
            entity.Property(e => e.TotalAmount).HasColumnType("DECIMAL");
            
            entity.HasOne(e => e.Customer)
                  .WithMany(c => c.Orders)
                  .HasForeignKey(e => e.CustomerId);
        });

        // Configure Customer
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(e => e.CustomerId);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique(); // Unique email
        });

        // Configure OrderItem
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(e => e.OrderItemId);
            entity.Property(e => e.UnitPrice).HasColumnType("DECIMAL");
            
            entity.HasOne(e => e.Order)
                  .WithMany(o => o.Items)
                  .HasForeignKey(e => e.OrderId);
                  
            entity.HasOne(e => e.Product)
                  .WithMany()
                  .HasForeignKey(e => e.ProductId);
        });

        // Seed data
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Laptop", Price = 999.99m, IsActive = true },
            new Product { Id = 2, Name = "Mouse", Price = 29.99m, IsActive = true },
            new Product { Id = 3, Name = "Keyboard", Price = 79.99m, IsActive = true }
        );
    }
}
```

### 3. Configure Services (ASP.NET Core / Console App)

#### ASP.NET Core (Program.cs)

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;
using MyApp.Data;

var builder = WebApplication.CreateBuilder(args);

// Add SharpCoreDB with dependency injection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB(
        "Data Source=./myapp.db;Password=MySecurePassword123;Cache=Shared",
        sharpOptions =>
        {
            // Additional SharpCoreDB-specific options can go here
        }));

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.EnsureCreatedAsync();
}

app.Run();
```

#### Console Application

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;
using MyApp.Data;

// Setup dependency injection
var services = new ServiceCollection();

services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB("Data Source=./myapp.db;Password=MySecurePassword123"));

var serviceProvider = services.BuildServiceProvider();

// Get DbContext and use it
using var context = serviceProvider.GetRequiredService<AppDbContext>();
await context.Database.EnsureCreatedAsync();

// Your application code...
```

---

## ?? Connection String Format

SharpCoreDB uses the following connection string format:

```
Data Source=<database_path>;Password=<encryption_password>[;Cache=Shared][;ReadOnly=true]
```

### Parameters

| Parameter | Required | Description | Example |
|-----------|----------|-------------|---------|
| `Data Source` | ? Yes | Path to database directory | `./myapp.db` or `C:\Data\myapp.db` |
| `Password` | ? Yes | AES-256-GCM encryption password | `MySecurePass123` |
| `Cache` | ? No | `Shared` = use connection pooling | `Cache=Shared` |
| `ReadOnly` | ? No | `true` = open in read-only mode | `ReadOnly=true` |

### Examples

```csharp
// Basic connection (encrypted, no pooling)
"Data Source=./mydb.db;Password=MyPassword"

// With connection pooling (recommended for web apps)
"Data Source=./mydb.db;Password=MyPassword;Cache=Shared"

// Read-only mode (for reporting/analytics)
"Data Source=./mydb.db;Password=MyPassword;ReadOnly=true"

// Full configuration
"Data Source=C:\\Data\\production.db;Password=P@ssw0rd!;Cache=Shared"
```

---

## ?? CRUD Operations

### Create (Insert)

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Single insert
var product = new Product
{
    Name = "New Laptop",
    Price = 1299.99m,
    IsActive = true,
    CreatedAt = DateTime.UtcNow
};

context.Products.Add(product);
await context.SaveChangesAsync();

Console.WriteLine($"Created product with ID: {product.Id}");

// Bulk insert (modern C# 14 collection expression)
var products = new List<Product>
{
    new() { Name = "Monitor", Price = 399.99m, IsActive = true },
    new() { Name = "Webcam", Price = 89.99m, IsActive = true },
    new() { Name = "Headset", Price = 149.99m, IsActive = true }
};

context.Products.AddRange(products);
await context.SaveChangesAsync();

Console.WriteLine($"Created {products.Count} products");
```

### Read (Query)

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Get all products
var allProducts = await context.Products.ToListAsync();

// Filter with Where
var activeProducts = await context.Products
    .Where(p => p.IsActive)
    .ToListAsync();

// Single or default
var product = await context.Products
    .FirstOrDefaultAsync(p => p.Id == 1);

// Complex query with ordering and pagination
var expensiveProducts = await context.Products
    .Where(p => p.Price > 500 && p.IsActive)
    .OrderByDescending(p => p.Price)
    .Take(10)
    .ToListAsync();

// Projection (select specific columns)
var productNames = await context.Products
    .Select(p => new { p.Id, p.Name, p.Price })
    .ToListAsync();

// Count and aggregates
var productCount = await context.Products.CountAsync();
var avgPrice = await context.Products.AverageAsync(p => p.Price);
var maxPrice = await context.Products.MaxAsync(p => p.Price);
var totalValue = await context.Products.SumAsync(p => p.Price);

Console.WriteLine($"Total: {productCount}, Avg: {avgPrice:C}, Max: {maxPrice:C}, Sum: {totalValue:C}");
```

### Update

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Find and update
var product = await context.Products.FindAsync(1);
if (product is not null)
{
    product.Price = 899.99m;
    product.IsActive = false;
    
    await context.SaveChangesAsync();
    Console.WriteLine($"Updated product {product.Name}");
}

// Bulk update (update all matching records)
var productsToUpdate = await context.Products
    .Where(p => p.Price < 50)
    .ToListAsync();

foreach (var p in productsToUpdate)
{
    p.IsActive = false;
}

await context.SaveChangesAsync();
Console.WriteLine($"Deactivated {productsToUpdate.Count} products");
```

### Delete

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Delete single entity
var product = await context.Products.FindAsync(1);
if (product is not null)
{
    context.Products.Remove(product);
    await context.SaveChangesAsync();
    Console.WriteLine($"Deleted product {product.Name}");
}

// Bulk delete
var oldProducts = await context.Products
    .Where(p => !p.IsActive)
    .ToListAsync();

context.Products.RemoveRange(oldProducts);
await context.SaveChangesAsync();

Console.WriteLine($"Deleted {oldProducts.Count} inactive products");
```

---

## ?? Relationships & Joins

### One-to-Many (Customer ? Orders)

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Create customer with orders
var customer = new Customer
{
    Name = "John Doe",
    Email = "john@example.com",
    Orders =
    [
        new Order
        {
            OrderDate = DateTime.UtcNow,
            TotalAmount = 1299.99m,
            Items =
            [
                new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 999.99m },
                new OrderItem { ProductId = 2, Quantity = 10, UnitPrice = 29.99m }
            ]
        }
    ]
};

context.Customers.Add(customer);
await context.SaveChangesAsync();

// Query with Include (eager loading)
var customersWithOrders = await context.Customers
    .Include(c => c.Orders)
    .ThenInclude(o => o.Items)
    .ThenInclude(i => i.Product)
    .ToListAsync();

foreach (var c in customersWithOrders)
{
    Console.WriteLine($"Customer: {c.Name}");
    foreach (var order in c.Orders)
    {
        Console.WriteLine($"  Order #{order.OrderId}: {order.TotalAmount:C}");
        foreach (var item in order.Items)
        {
            Console.WriteLine($"    - {item.Product?.Name}: {item.Quantity} x {item.UnitPrice:C}");
        }
    }
}
```

### Manual Joins

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Join query
var orderSummary = await context.Orders
    .Join(context.Customers,
          order => order.CustomerId,
          customer => customer.CustomerId,
          (order, customer) => new
          {
              OrderId = order.OrderId,
              CustomerName = customer.Name,
              OrderDate = order.OrderDate,
              TotalAmount = order.TotalAmount
          })
    .ToListAsync();

foreach (var summary in orderSummary)
{
    Console.WriteLine($"{summary.CustomerName}: Order #{summary.OrderId} - {summary.TotalAmount:C}");
}
```

---

## ?? Advanced Features

### Transactions

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Explicit transaction
using var transaction = await context.Database.BeginTransactionAsync();

try
{
    var product = new Product { Name = "Test Product", Price = 99.99m, IsActive = true };
    context.Products.Add(product);
    await context.SaveChangesAsync();

    var customer = new Customer { Name = "Test Customer", Email = "test@test.com" };
    context.Customers.Add(customer);
    await context.SaveChangesAsync();

    await transaction.CommitAsync();
    Console.WriteLine("Transaction committed successfully");
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    Console.WriteLine($"Transaction failed: {ex.Message}");
}

// Implicit transaction (SaveChanges is atomic)
var products = new List<Product>
{
    new() { Name = "Product 1", Price = 10m, IsActive = true },
    new() { Name = "Product 2", Price = 20m, IsActive = true }
};

context.Products.AddRange(products);
await context.SaveChangesAsync(); // All or nothing
```

### Async Operations

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// All operations support async
await context.Products.AddAsync(new Product { Name = "Async Product", Price = 49.99m, IsActive = true });
await context.SaveChangesAsync();

var products = await context.Products.ToListAsync();
var product = await context.Products.FindAsync(1);
var count = await context.Products.CountAsync();

// Async LINQ operations
var expensiveProducts = await context.Products
    .Where(p => p.Price > 100)
    .OrderByDescending(p => p.Price)
    .ToListAsync();
```

### Raw SQL Queries

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Execute raw SQL (modern C# 14 collection expression for parameters)
var minPrice = 100m;
var products = await context.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Price > {0}", minPrice)
    .ToListAsync();

// Execute non-query SQL
var rowsAffected = await context.Database
    .ExecuteSqlRawAsync("UPDATE Products SET IsActive = 0 WHERE Price < {0}", 50m);

Console.WriteLine($"Deactivated {rowsAffected} products");
```

### Tracking vs No-Tracking

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// Default: tracking (for updates)
var trackedProduct = await context.Products.FindAsync(1);
trackedProduct!.Price = 999.99m;
await context.SaveChangesAsync(); // Changes are tracked and saved

// No tracking (read-only, better performance)
var readOnlyProducts = await context.Products
    .AsNoTracking()
    .ToListAsync();

// No changes will be saved
readOnlyProducts[0].Price = 0m;
await context.SaveChangesAsync(); // Nothing happens
```

---

## ?? Configuration & Performance

### High-Performance Configuration

```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseSharpCoreDB(
        "Data Source=./myapp.db;Password=SecurePass;Cache=Shared",
        sharpOptions =>
        {
            // SharpCoreDB-specific configuration would go here
            // (Future enhancement: expose DatabaseConfig through options)
        });

    // EF Core configuration
    options.EnableSensitiveDataLogging(false); // Disable in production
    options.EnableDetailedErrors(false); // Disable in production
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
});
```

### Bulk Operations (High-Speed Inserts)

```csharp
using var context = serviceProvider.GetRequiredService<AppDbContext>();

// For large bulk inserts, use AddRange and SaveChanges in batches
var batchSize = 1000;
var products = Enumerable.Range(1, 10000)
    .Select(i => new Product
    {
        Name = $"Product {i}",
        Price = i * 10m,
        IsActive = true
    })
    .ToList();

for (int i = 0; i < products.Count; i += batchSize)
{
    var batch = products.Skip(i).Take(batchSize);
    context.Products.AddRange(batch);
    await context.SaveChangesAsync();
    
    Console.WriteLine($"Inserted batch {i / batchSize + 1}");
}
```

### Connection Pooling

```csharp
// Enable connection pooling in connection string
services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB("Data Source=./myapp.db;Password=SecurePass;Cache=Shared"));

// Pool is automatically managed by SharpCoreDB's DatabasePool
// Connections are reused across requests in ASP.NET Core
```

---

## ?? Security Best Practices

### 1. Secure Password Management

```csharp
// ? BAD: Hardcoded password
options.UseSharpCoreDB("Data Source=./db;Password=MyPassword123");

// ? GOOD: Use environment variables
var password = Environment.GetEnvironmentVariable("SHARPCOREDB_PASSWORD") 
               ?? throw new InvalidOperationException("Password not configured");
options.UseSharpCoreDB($"Data Source=./db;Password={password}");

// ? BETTER: Use .NET Secret Manager (development) or Azure Key Vault (production)
var connectionString = builder.Configuration.GetConnectionString("SharpCoreDB");
options.UseSharpCoreDB(connectionString);
```

### 2. Read-Only Access for Reporting

```csharp
// Reporting context (read-only)
services.AddDbContext<ReportingDbContext>(options =>
    options.UseSharpCoreDB("Data Source=./myapp.db;Password=SecurePass;ReadOnly=true"));

// This prevents accidental writes in reporting code
```

### 3. Parameterized Queries (Prevent SQL Injection)

```csharp
// ? SAFE: Entity Framework uses parameterized queries by default
var products = await context.Products
    .Where(p => p.Name == userInput) // Automatically parameterized
    .ToListAsync();

// ? SAFE: FromSqlRaw with parameters
var products = await context.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Name = {0}", userInput)
    .ToListAsync();

// ? DANGEROUS: String concatenation
// var products = await context.Products
//     .FromSqlRaw($"SELECT * FROM Products WHERE Name = '{userInput}'") // SQL injection risk!
//     .ToListAsync();
```

---

## ?? Testing

### In-Memory Testing (for unit tests)

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Data;

public class ProductServiceTests
{
    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task AddProduct_ShouldIncreaseCount()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var product = new Product { Name = "Test", Price = 99.99m, IsActive = true };

        // Act
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Assert
        var count = await context.Products.CountAsync();
        Assert.Equal(1, count);
    }
}
```

### Integration Testing (with real SharpCoreDB)

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;
using MyApp.Data;

public class IntegrationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly AppDbContext _context;

    public IntegrationTests()
    {
        _testDbPath = $"./test_{Guid.NewGuid()}";
        
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSharpCoreDB($"Data Source={_testDbPath};Password=TestPassword123")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task RealDatabase_CrudOperations_Work()
    {
        // Arrange
        var product = new Product { Name = "Test Product", Price = 49.99m, IsActive = true };

        // Act & Assert
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        Assert.True(product.Id > 0);

        var retrieved = await _context.Products.FindAsync(product.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test Product", retrieved.Name);

        retrieved.Price = 59.99m;
        await _context.SaveChangesAsync();

        var updated = await _context.Products.FindAsync(product.Id);
        Assert.Equal(59.99m, updated!.Price);

        _context.Products.Remove(updated);
        await _context.SaveChangesAsync();

        var deleted = await _context.Products.FindAsync(product.Id);
        Assert.Null(deleted);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }
}
```

---

## ?? Data Types Mapping

| .NET Type | SharpCoreDB Type | Notes |
|-----------|------------------|-------|
| `int` | `INTEGER` | 32-bit integer |
| `long` | `LONG` | 64-bit integer |
| `string` | `TEXT` | Variable length text |
| `bool` | `BOOLEAN` | True/False |
| `DateTime` | `DATETIME` | Date and time |
| `decimal` | `DECIMAL` | High-precision decimal |
| `double` | `REAL` | Floating point |
| `Guid` | `GUID` | Globally unique identifier |
| `byte[]` | `BLOB` | Binary data |

---

## ?? Complete Example: E-Commerce Application

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;

// Setup
var services = new ServiceCollection();
services.AddDbContext<ECommerceDbContext>(options =>
    options.UseSharpCoreDB("Data Source=./ecommerce.db;Password=SecurePassword123;Cache=Shared"));

var serviceProvider = services.BuildServiceProvider();
using var context = serviceProvider.GetRequiredService<ECommerceDbContext>();

await context.Database.EnsureCreatedAsync();

// Create sample data
var customer = new Customer
{
    Name = "Alice Johnson",
    Email = "alice@example.com"
};

var products = new List<Product>
{
    new() { Name = "Laptop", Price = 1299.99m, IsActive = true },
    new() { Name = "Mouse", Price = 29.99m, IsActive = true },
    new() { Name = "Keyboard", Price = 79.99m, IsActive = true }
};

context.Customers.Add(customer);
context.Products.AddRange(products);
await context.SaveChangesAsync();

// Create order
var order = new Order
{
    CustomerId = customer.CustomerId,
    OrderDate = DateTime.UtcNow,
    Items = products.Select(p => new OrderItem
    {
        ProductId = p.Id,
        Quantity = 1,
        UnitPrice = p.Price
    }).ToList()
};

order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);

context.Orders.Add(order);
await context.SaveChangesAsync();

// Query orders with customer and products
var orders = await context.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .ThenInclude(i => i.Product)
    .ToListAsync();

foreach (var o in orders)
{
    Console.WriteLine($"Order #{o.OrderId} - Customer: {o.Customer?.Name}");
    Console.WriteLine($"  Date: {o.OrderDate:yyyy-MM-dd}");
    Console.WriteLine($"  Items:");
    
    foreach (var item in o.Items)
    {
        Console.WriteLine($"    - {item.Product?.Name}: {item.Quantity} x {item.UnitPrice:C} = {item.Quantity * item.UnitPrice:C}");
    }
    
    Console.WriteLine($"  Total: {o.TotalAmount:C}");
}

// DbContext definition
public class ECommerceDbContext(DbContextOptions<ECommerceDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure entities...
        modelBuilder.Entity<Product>().ToTable("Products");
        modelBuilder.Entity<Order>().ToTable("Orders");
        modelBuilder.Entity<Customer>().ToTable("Customers");
        modelBuilder.Entity<OrderItem>().ToTable("OrderItems");
    }
}
```

---

## ?? Troubleshooting

### Common Issues

**Issue**: `InvalidOperationException: Connection string must be configured.`

**Solution**: Ensure you're calling `UseSharpCoreDB()` with a valid connection string:
```csharp
options.UseSharpCoreDB("Data Source=./mydb.db;Password=MyPassword");
```

---

**Issue**: Database file not found or access denied.

**Solution**:
- Ensure the database path exists or is writable
- Use absolute paths: `Data Source=C:\\Data\\myapp.db;...`
- Check file permissions

---

**Issue**: Slow query performance.

**Solution**:
- Use `AsNoTracking()` for read-only queries
- Enable connection pooling: `Cache=Shared`
- Add indexes to frequently queried columns
- Use projection (`Select`) instead of loading entire entities

---

**Issue**: `NotImplementedException` when using certain LINQ methods.

**Solution**:
- SharpCoreDB supports most common LINQ operations
- For advanced queries, use `FromSqlRaw()` with raw SQL
- Check the query is being executed on the database, not in-memory

---

## ?? Additional Resources

- **SharpCoreDB Core Documentation**: See main SharpCoreDB README.md
- **Entity Framework Core Docs**: https://docs.microsoft.com/ef/core/
- **GitHub Repository**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **Issue Tracker**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

---

## ?? Comparison with Other Providers

| Feature | SharpCoreDB.EF | SQLite.EF | SQL Server.EF |
|---------|----------------|-----------|---------------|
| **Built-in Encryption** | ? AES-256-GCM | ? No | ? TDE (Enterprise) |
| **Pure .NET** | ? Yes | ? Native library | ? Native library |
| **File-based** | ? Yes | ? Yes | ? Server-based |
| **Connection Pooling** | ? Yes | ? Yes | ? Yes |
| **Transactions** | ? Yes | ? Yes | ? Yes |
| **Async Operations** | ? Full support | ? Full support | ? Full support |
| **LINQ Support** | ? Most operations | ? Comprehensive | ? Comprehensive |
| **Cross-platform** | ? Yes (.NET 10) | ? Yes | ? Yes (.NET) |

---

## ?? What Makes SharpCoreDB Unique?

1. **Built-in AES-256-GCM Encryption** - No separate encryption libraries needed
2. **Pure .NET Implementation** - No P/Invoke overhead, fully managed code
3. **Modern C# 14 Patterns** - Primary constructors, collection expressions, pattern matching
4. **High-Performance Concurrency** - Optimized for multi-threaded workloads
5. **SIMD-Accelerated Aggregates** - 50-106x faster than LINQ for analytics
6. **Hash Index Support** - O(1) lookups for frequently queried columns
7. **MVCC Snapshot Isolation** - ACID-compliant transactions with minimal locking

---

## ? Production Checklist

Before deploying to production:

- [ ] Use environment variables or secret management for passwords
- [ ] Enable connection pooling (`Cache=Shared`)
- [ ] Disable sensitive data logging
- [ ] Test backup/restore procedures
- [ ] Monitor query performance
- [ ] Implement proper error handling
- [ ] Use read-only connections for reporting
- [ ] Set up automated database backups
- [ ] Test failover scenarios
- [ ] Document connection string configuration

---

**Happy Coding with SharpCoreDB + Entity Framework Core!** ??

*Built with ?? by MPCoreDeveloper & GitHub Copilot*
