# SharpCoreDB.EntityFrameworkCore - Complete Usage Guide

Volledige gids voor het gebruik van Entity Framework Core met SharpCoreDB.

## ?? Inhoudsopgave

1. [Installatie](#installatie)
2. [Quick Start](#quick-start)
3. [DbContext Configuratie](#dbcontext-configuratie)
4. [Entities & Relationships](#entities--relationships)
5. [CRUD Operaties](#crud-operaties)
6. [Advanced Queries](#advanced-queries)
7. [Dependency Injection](#dependency-injection)
8. [Migrations](#migrations)
9. [Performance Optimization](#performance-optimization)
10. [Security & Encryption](#security--encryption)
11. [Testing](#testing)
12. [Best Practices](#best-practices)

---

## ?? Installatie

### Stap 1: Installeer NuGet Packages

```bash
# Basis packages
dotnet add package SharpCoreDB.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Design

# Voor migrations
dotnet tool install --global dotnet-ef
```

### Stap 2: Verify Installation

```bash
dotnet ef --version
# Output: Entity Framework Core .NET Command-line Tools 10.0.1
```

---

## ?? Quick Start

### Minimaal Werkend Voorbeeld

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

// 1. Entity
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

// 2. DbContext
public class ShopContext : DbContext
{
    public DbSet<Product> Products { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSharpCoreDB("Data Source=shop.db");
    }
}

// 3. Gebruik
using var db = new ShopContext();
db.Database.EnsureCreated();

// Create
db.Products.Add(new Product { Name = "Laptop", Price = 999.99m });
db.SaveChanges();

// Read
var products = db.Products.ToList();
foreach (var p in products)
{
    Console.WriteLine($"{p.Name}: ${p.Price}");
}
```

---

## ?? DbContext Configuratie

### Basis Configuratie

```csharp
public class AppDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB("Data Source=app.db");
    }
}
```

### Geavanceerde Configuratie

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.UseSharpCoreDB(
        connectionString: "Data Source=app.db;Encryption=true;Password=SecurePass123",
        sharpCoreOptions => 
        {
            // Encryption
            sharpCoreOptions.UseEncryption(true);
            sharpCoreOptions.SetPassword("SecurePass123");
            
            // Performance
            sharpCoreOptions.SetCacheSize(100); // MB
            sharpCoreOptions.SetPageSize(4096); // bytes
            sharpCoreOptions.CommandTimeout(30); // seconds
            
            // Retry logic
            sharpCoreOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5)
            );
            
            // Logging (development only)
            sharpCoreOptions.EnableSensitiveDataLogging(
                isDevelopment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
            );
        }
    );
    
    // EF Core logging
    optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
    optionsBuilder.EnableDetailedErrors();
}
```

### Connection String Options

```csharp
var connectionString = new SharpCoreConnectionStringBuilder
{
    DataSource = "database.db",
    Encryption = true,
    Password = "MyPassword",
    CacheSize = 100, // MB
    PageSize = 4096,
    Timeout = 30,
    Pooling = true,
    MaxPoolSize = 100
}.ToString();

optionsBuilder.UseSharpCoreDB(connectionString);
```

---

## ?? Entities & Relationships

### One-to-Many Relationship

```csharp
public class Blog
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    
    // Navigation property
    public List<Post> Posts { get; set; } = new();
}

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    
    // Foreign key
    public int BlogId { get; set; }
    
    // Navigation property
    public Blog? Blog { get; set; }
}

// DbContext configuration
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>()
        .HasMany(b => b.Posts)
        .WithOne(p => p.Blog)
        .HasForeignKey(p => p.BlogId)
        .OnDelete(DeleteBehavior.Cascade);
}
```

### Many-to-Many Relationship

```csharp
public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public List<Course> Courses { get; set; } = new();
}

public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    
    public List<Student> Students { get; set; } = new();
}

// EF Core 5+ automatically creates join table
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Student>()
        .HasMany(s => s.Courses)
        .WithMany(c => c.Students)
        .UsingEntity(j => j.ToTable("StudentCourses"));
}
```

### One-to-One Relationship

```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    
    public UserProfile? Profile { get; set; }
}

public class UserProfile
{
    public int Id { get; set; }
    public string Bio { get; set; } = string.Empty;
    
    public int UserId { get; set; }
    public User? User { get; set; }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<User>()
        .HasOne(u => u.Profile)
        .WithOne(p => p.User)
        .HasForeignKey<UserProfile>(p => p.UserId);
}
```

### Owned Types (Value Objects)

```csharp
public class Order
{
    public int Id { get; set; }
    public Address ShippingAddress { get; set; } = new();
}

public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .OwnsOne(o => o.ShippingAddress);
}
```

---

## ?? CRUD Operaties

### Create (Insert)

```csharp
// Single insert
var product = new Product { Name = "Laptop", Price = 999.99m };
db.Products.Add(product);
await db.SaveChangesAsync();

// Bulk insert
var products = new List<Product>
{
    new() { Name = "Mouse", Price = 29.99m },
    new() { Name = "Keyboard", Price = 79.99m },
    new() { Name = "Monitor", Price = 299.99m }
};
db.Products.AddRange(products);
await db.SaveChangesAsync();

// Insert with related data
var blog = new Blog 
{ 
    Title = "My Blog",
    Posts = new List<Post>
    {
        new() { Content = "First post" },
        new() { Content = "Second post" }
    }
};
db.Blogs.Add(blog);
await db.SaveChangesAsync();
```

### Read (Select)

```csharp
// Get all
var allProducts = await db.Products.ToListAsync();

// Get by ID
var product = await db.Products.FindAsync(1);

// Get single (throws if not found or multiple)
var expensive = await db.Products.SingleAsync(p => p.Price > 1000);

// Get single or default
var maybeProduct = await db.Products.SingleOrDefaultAsync(p => p.Id == 999);

// Get first
var cheapest = await db.Products.OrderBy(p => p.Price).FirstAsync();

// Check existence
bool hasExpensive = await db.Products.AnyAsync(p => p.Price > 1000);

// Count
int count = await db.Products.CountAsync();

// With filtering
var filtered = await db.Products
    .Where(p => p.Price > 100 && p.Price < 500)
    .OrderByDescending(p => p.Price)
    .Take(10)
    .ToListAsync();
```

### Update

```csharp
// Update single entity
var product = await db.Products.FindAsync(1);
if (product != null)
{
    product.Price = 899.99m;
    await db.SaveChangesAsync();
}

// Update multiple (EF Core 7+)
await db.Products
    .Where(p => p.Price < 100)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 1.1m));

// Attach and update (disconnected scenario)
var updatedProduct = new Product { Id = 1, Name = "Updated", Price = 799.99m };
db.Products.Update(updatedProduct);
await db.SaveChangesAsync();
```

### Delete

```csharp
// Delete single
var product = await db.Products.FindAsync(1);
if (product != null)
{
    db.Products.Remove(product);
    await db.SaveChangesAsync();
}

// Delete multiple (EF Core 7+)
await db.Products
    .Where(p => p.Price < 10)
    .ExecuteDeleteAsync();

// Delete range
var oldProducts = await db.Products.Where(p => p.Price < 50).ToListAsync();
db.Products.RemoveRange(oldProducts);
await db.SaveChangesAsync();
```

---

## ?? Advanced Queries

### Joins

```csharp
// Inner join with Include
var blogsWithPosts = await db.Blogs
    .Include(b => b.Posts)
    .ToListAsync();

// Multiple includes
var posts = await db.Posts
    .Include(p => p.Blog)
    .Include(p => p.Author)
    .ToListAsync();

// Nested include (ThenInclude)
var blogs = await db.Blogs
    .Include(b => b.Posts)
        .ThenInclude(p => p.Comments)
    .ToListAsync();

// Manual join
var query = from blog in db.Blogs
            join post in db.Posts on blog.Id equals post.BlogId
            where blog.Title.Contains("Tech")
            select new { blog.Title, post.Content };

var results = await query.ToListAsync();
```

### Grouping & Aggregation

```csharp
// Group by
var productsByCategory = await db.Products
    .GroupBy(p => p.Category)
    .Select(g => new
    {
        Category = g.Key,
        Count = g.Count(),
        AveragePrice = g.Average(p => p.Price),
        TotalValue = g.Sum(p => p.Price * p.Stock)
    })
    .ToListAsync();

// Having clause
var popularCategories = await db.Products
    .GroupBy(p => p.Category)
    .Where(g => g.Count() > 10)
    .Select(g => new { Category = g.Key, Count = g.Count() })
    .ToListAsync();
```

### Subqueries

```csharp
// Products above average price
var avgPrice = await db.Products.AverageAsync(p => p.Price);
var expensive = await db.Products
    .Where(p => p.Price > avgPrice)
    .ToListAsync();

// Or in single query
var expensiveProducts = await db.Products
    .Where(p => p.Price > db.Products.Average(x => x.Price))
    .ToListAsync();

// Correlated subquery
var blogsWithManyPosts = await db.Blogs
    .Where(b => db.Posts.Count(p => p.BlogId == b.Id) > 5)
    .ToListAsync();
```

### Raw SQL

```csharp
// Raw SQL query
var products = await db.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Price > {0}", 100)
    .ToListAsync();

// With interpolation (safe from SQL injection)
var minPrice = 100m;
var products2 = await db.Products
    .FromSqlInterpolated($"SELECT * FROM Products WHERE Price > {minPrice}")
    .ToListAsync();

// Execute command
await db.Database.ExecuteSqlRawAsync(
    "UPDATE Products SET Price = Price * 1.1 WHERE CategoryId = {0}", 
    categoryId);
```

### Pagination

```csharp
public async Task<PagedResult<Product>> GetProductsPagedAsync(int page, int pageSize)
{
    var query = db.Products.OrderBy(p => p.Id);
    
    var total = await query.CountAsync();
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    
    return new PagedResult<Product>
    {
        Items = items,
        TotalCount = total,
        Page = page,
        PageSize = pageSize,
        TotalPages = (int)Math.Ceiling(total / (double)pageSize)
    };
}
```

---

## ?? Dependency Injection

### ASP.NET Core Setup

```csharp
// Program.cs (.NET 6+)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSharpCoreDB(connectionString);
});

// Enable pooling for better performance
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSharpCoreDB(connectionString));

var app = builder.Build();
```

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db;Encryption=true;Password=SecurePass"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### Controller Usage

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(AppDbContext context, ILogger<ProductsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Product>>> GetProducts()
    {
        return await _context.Products.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        
        if (product == null)
            return NotFound();
        
        return product;
    }

    [HttpPost]
    public async Task<ActionResult<Product>> CreateProduct(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, Product product)
    {
        if (id != product.Id)
            return BadRequest();
        
        _context.Entry(product).State = EntityState.Modified;
        
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id))
                return NotFound();
            throw;
        }
        
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();
        
        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        
        return NoContent();
    }
}
```

---

## ?? Migrations

### Create Migration

```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Create migration for changes
dotnet ef migrations add AddProductCategory

# With specific context
dotnet ef migrations add InitialCreate --context AppDbContext
```

### Apply Migration

```bash
# Update to latest
dotnet ef database update

# Update to specific migration
dotnet ef database update AddProductCategory

# Rollback to previous
dotnet ef database update PreviousMigrationName
```

### Remove Migration

```bash
# Remove last migration (only if not applied)
dotnet ef migrations remove
```

### Generate SQL Script

```bash
# Generate script for all migrations
dotnet ef migrations script

# Generate script for specific range
dotnet ef migrations script FromMigration ToMigration

# Output to file
dotnet ef migrations script > migration.sql
```

### Migration Code Example

```csharp
public partial class AddProductCategory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "Products",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_Products_Category",
            table: "Products",
            column: "Category");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Products_Category",
            table: "Products");

        migrationBuilder.DropColumn(
            name: "Category",
            table: "Products");
    }
}
```

---

## ? Performance Optimization

### 1. AsNoTracking for Read-Only

```csharp
// Faster for read-only scenarios
var products = await db.Products
    .AsNoTracking()
    .ToListAsync();
```

### 2. Compiled Queries

```csharp
private static readonly Func<AppDbContext, int, Task<Product?>> _getProductById =
    EF.CompileAsyncQuery((AppDbContext db, int id) =>
        db.Products.FirstOrDefault(p => p.Id == id));

// Usage
var product = await _getProductById(db, 123);
```

### 3. Split Queries

```csharp
// Avoid cartesian explosion with multiple includes
var blogs = await db.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Authors)
    .AsSplitQuery() // Separate SQL queries
    .ToListAsync();
```

### 4. Select Only Needed Columns

```csharp
// Bad: Loads all columns
var products = await db.Products.ToListAsync();

// Good: Only specific columns
var productNames = await db.Products
    .Select(p => new { p.Id, p.Name })
    .ToListAsync();
```

### 5. Batch Operations

```csharp
// Configure batch size
optionsBuilder.UseSharpCoreDB(connectionString, options =>
{
    options.MaxBatchSize(100);
});
```

### 6. Connection Pooling

```csharp
builder.Services.AddDbContextPool<AppDbContext>(
    options => options.UseSharpCoreDB(connectionString),
    poolSize: 128);
```

---

## ?? Security & Encryption

### Encrypted Database

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    options.UseSharpCoreDB(
        "Data Source=secure.db;Encryption=true;Password=VerySecurePassword123!;EncryptionAlgorithm=AES-256-GCM"
    );
}
```

### Secure Password Management

```csharp
// Use environment variables
var password = Environment.GetEnvironmentVariable("DB_PASSWORD");
var connectionString = $"Data Source=app.db;Encryption=true;Password={password}";

// Or Azure Key Vault / AWS Secrets Manager
var secretClient = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());
var secret = await secretClient.GetSecretAsync("DatabasePassword");
var password = secret.Value.Value;
```

### Row-Level Security (Manual)

```csharp
// Filter by user
public class SecureDbContext : DbContext
{
    private readonly string _currentUserId;

    public SecureDbContext(string currentUserId)
    {
        _currentUserId = currentUserId;
    }

    public DbSet<Document> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Global query filter
        modelBuilder.Entity<Document>()
            .HasQueryFilter(d => d.OwnerId == _currentUserId);
    }
}
```

---

## ?? Testing

### Unit Tests met In-Memory Database

```csharp
using Microsoft.EntityFrameworkCore;
using Xunit;

public class ProductServiceTests
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSharpCoreDB(":memory:") // In-memory database
            .Options;
        
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task CanAddProduct()
    {
        // Arrange
        using var context = CreateContext();
        var service = new ProductService(context);
        
        // Act
        var product = new Product { Name = "Test", Price = 99.99m };
        await service.AddProductAsync(product);
        
        // Assert
        var count = await context.Products.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CanFindProductByName()
    {
        // Arrange
        using var context = CreateContext();
        context.Products.Add(new Product { Name = "Laptop", Price = 999m });
        await context.SaveChangesAsync();
        
        // Act
        var product = await context.Products
            .FirstOrDefaultAsync(p => p.Name == "Laptop");
        
        // Assert
        Assert.NotNull(product);
        Assert.Equal(999m, product.Price);
    }
}
```

### Integration Tests

```csharp
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_ReturnsSuccessAndCorrectContentType()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json; charset=utf-8", 
            response.Content.Headers.ContentType.ToString());
    }
}
```

---

## ? Best Practices

### 1. Dispose DbContext Properly

```csharp
// Good: using statement
using (var context = new AppDbContext())
{
    // Use context
}

// Good: using declaration (.NET 8+)
using var context = new AppDbContext();
// Auto-disposed at end of scope
```

### 2. Async All The Way

```csharp
// Good
var products = await db.Products.ToListAsync();

// Bad (blocks thread)
var products = db.Products.ToList();
```

### 3. Use Transactions for Multiple Operations

```csharp
using var transaction = await db.Database.BeginTransactionAsync();
try
{
    // Multiple operations
    db.Products.Add(product1);
    db.Products.Add(product2);
    await db.SaveChangesAsync();
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### 4. Eager Load Related Data

```csharp
// Good: Single query
var blogs = await db.Blogs
    .Include(b => b.Posts)
    .ToListAsync();

// Bad: N+1 queries
var blogs = await db.Blogs.ToListAsync();
foreach (var blog in blogs)
{
    var posts = blog.Posts.ToList(); // Separate query for each blog!
}
```

### 5. Use Indexes

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .HasIndex(p => p.Name);
    
    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.CustomerId, o.OrderDate }); // Composite index
}
```

---

## ?? Troubleshooting

### Common Errors

**Error: "A second operation started on this context before a previous operation completed"**
```csharp
// Solution: Don't reuse DbContext across async operations
// Use a new context or add await properly
```

**Error: "The instance of entity type cannot be tracked"**
```csharp
// Solution: Detach before attaching new
db.Entry(entity).State = EntityState.Detached;
```

**Error: "Slow query performance"**
```csharp
// Solutions:
// 1. Add indexes
// 2. Use AsNoTracking() for read-only
// 3. Use compiled queries
// 4. Check with .LogTo() for generated SQL
```

---

## ?? Meer Resources

- **SharpCoreDB Main**: https://www.nuget.org/packages/SharpCoreDB
- **EF Core Docs**: https://docs.microsoft.com/ef/core/
- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **Issues**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

---

**Happy coding with SharpCoreDB.EntityFrameworkCore!** ??
