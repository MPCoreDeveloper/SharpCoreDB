using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SharpCoreDB.EntityFrameworkCore.Examples;

/// <summary>
/// ASP.NET Core API example using SharpCoreDB with Entity Framework Core.
/// 
/// NOTE: This is a documentation/reference example showing how to use SharpCoreDB.EntityFrameworkCore
/// in an ASP.NET Core application. To run this code:
/// 
/// 1. Create a new ASP.NET Core project:
///    dotnet new webapi -n MyApp
/// 
/// 2. Add required packages:
///    dotnet add package SharpCoreDB.EntityFrameworkCore
///    dotnet add package Swashbuckle.AspNetCore
/// 
/// 3. Copy this code to Program.cs and build
/// 
/// This example demonstrates:
/// - Minimal API endpoints
/// - CRUD operations
/// - Entity relationships
/// - Modern C# 14 patterns
/// - RESTful API design
/// </summary>
public class AspNetCoreExample
{
    // Example code for Program.cs in ASP.NET Core application
    public static string GetExampleProgramCs()
    {
        return @"
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add SharpCoreDB with EF Core
builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSharpCoreDB(
        builder.Configuration.GetConnectionString(""SharpCoreDB"") 
        ?? ""Data Source=./products.db;Password=SecurePassword123;Cache=Shared""));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    await context.Database.EnsureCreatedAsync();
}

// API Endpoints
app.MapGet(""/api/products"", async (ProductDbContext db) =>
    await db.Products.Include(p => p.Category).AsNoTracking().ToListAsync())
    .WithName(""GetAllProducts"");

app.MapGet(""/api/products/{id}"", async (int id, ProductDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    return product is not null ? Results.Ok(product) : Results.NotFound();
})
.WithName(""GetProductById"");

app.MapPost(""/api/products"", async (CreateProductRequest request, ProductDbContext db) =>
{
    var product = new Product
    {
        Name = request.Name,
        Price = request.Price,
        CategoryId = request.CategoryId
    };
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($""/api/products/{product.Id}"", product);
})
.WithName(""CreateProduct"");

app.Run();
";
    }

    // Standalone console example that works without ASP.NET Core
    public static async Task RunStandaloneExample()
    {
        Console.WriteLine("?? SharpCoreDB.EntityFrameworkCore - Standalone Console Example\n");

        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddDbContext<ProductDbContext>(options =>
            options.UseSharpCoreDB("Data Source=./products.db;Password=SecurePassword123;Cache=Shared"));

        var serviceProvider = services.BuildServiceProvider();

        using var context = serviceProvider.GetRequiredService<ProductDbContext>();
        
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();

        // Seed data
        await SeedData(context);

        // Demonstrate CRUD operations
        await DemonstrateCrudOperations(context);

        // Demonstrate relationships
        await DemonstrateRelationships(context);

        Console.WriteLine("\n? Example completed successfully!");
    }

    private static async Task SeedData(ProductDbContext context)
    {
        if (!await context.Categories.AnyAsync())
        {
            Console.WriteLine("?? Seeding database...\n");

            var categories = new List<Category>
            {
                new() { Name = "Electronics", Description = "Electronic devices and accessories", CreatedAt = DateTime.UtcNow },
                new() { Name = "Books", Description = "Books and publications", CreatedAt = DateTime.UtcNow },
                new() { Name = "Clothing", Description = "Apparel and fashion", CreatedAt = DateTime.UtcNow }
            };

            context.Categories.AddRange(categories);
            await context.SaveChangesAsync();

            var products = new List<Product>
            {
                new() { Name = "Laptop", Description = "High-performance laptop", Price = 1299.99m, Stock = 10, IsActive = true, CategoryId = 1, CreatedAt = DateTime.UtcNow },
                new() { Name = "Mouse", Description = "Wireless mouse", Price = 29.99m, Stock = 50, IsActive = true, CategoryId = 1, CreatedAt = DateTime.UtcNow },
                new() { Name = "Keyboard", Description = "Mechanical keyboard", Price = 79.99m, Stock = 30, IsActive = true, CategoryId = 1, CreatedAt = DateTime.UtcNow },
                new() { Name = "C# Programming", Description = "Learn C# programming", Price = 49.99m, Stock = 20, IsActive = true, CategoryId = 2, CreatedAt = DateTime.UtcNow },
                new() { Name = "T-Shirt", Description = "Cotton t-shirt", Price = 19.99m, Stock = 100, IsActive = true, CategoryId = 3, CreatedAt = DateTime.UtcNow }
            };

            context.Products.AddRange(products);
            await context.SaveChangesAsync();

            Console.WriteLine($"? Seeded {categories.Count} categories and {products.Count} products\n");
        }
    }

    private static async Task DemonstrateCrudOperations(ProductDbContext context)
    {
        Console.WriteLine("?? CRUD Operations:\n");

        // CREATE
        var newProduct = new Product
        {
            Name = "Webcam",
            Description = "HD webcam",
            Price = 89.99m,
            Stock = 15,
            IsActive = true,
            CategoryId = 1,
            CreatedAt = DateTime.UtcNow
        };

        context.Products.Add(newProduct);
        await context.SaveChangesAsync();
        Console.WriteLine($"? Created: {newProduct.Name} (ID: {newProduct.Id})");

        // READ
        var product = await context.Products.FindAsync(newProduct.Id);
        Console.WriteLine($"? Read: {product?.Name} - {product?.Price:C}");

        // UPDATE
        if (product is not null)
        {
            product.Price = 79.99m;
            await context.SaveChangesAsync();
            Console.WriteLine($"? Updated: {product.Name} - New price: {product.Price:C}");
        }

        // DELETE (will delete later)
        Console.WriteLine($"? Product ready for deletion: {product?.Name}\n");
    }

    private static async Task DemonstrateRelationships(ProductDbContext context)
    {
        Console.WriteLine("?? Relationships (Category ? Products):\n");

        // Query with Include (eager loading)
        var categories = await context.Categories
            .Include(c => c.Products)
            .AsNoTracking()
            .ToListAsync();

        foreach (var category in categories)
        {
            Console.WriteLine($"?? {category.Name} ({category.Products.Count} products):");
            foreach (var product in category.Products.Take(3))
            {
                Console.WriteLine($"   - {product.Name}: {product.Price:C} (Stock: {product.Stock})");
            }
            Console.WriteLine();
        }
    }
}

// ============================================================
// ENTITY MODELS
// ============================================================

/// <summary>
/// Product entity with relationship to Category.
/// </summary>
public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "DECIMAL")]
    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    // Foreign key
    public int CategoryId { get; set; }

    // Navigation property
    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }
}

/// <summary>
/// Category entity with one-to-many relationship to Products.
/// </summary>
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Navigation property
    public ICollection<Product> Products { get; set; } = [];
}

// ============================================================
// REQUEST/RESPONSE DTOs (for ASP.NET Core)
// ============================================================

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId
);

public record UpdateProductRequest(
    string? Name = null,
    string? Description = null,
    decimal? Price = null,
    int? Stock = null,
    bool? IsActive = null,
    int? CategoryId = null
);

public record CreateCategoryRequest(
    string Name,
    string Description
);

// ============================================================
// DB CONTEXT
// ============================================================

/// <summary>
/// Product database context using SharpCoreDB.
/// Modern C# 14 with primary constructors.
/// </summary>
public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Category
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();

            // Relationship
            entity.HasMany(e => e.Products)
                  .WithOne(p => p.Category)
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Product
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("DECIMAL");
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.CategoryId);
        });
    }
}
