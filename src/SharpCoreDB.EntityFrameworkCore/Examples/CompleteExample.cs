using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SharpCoreDB.EntityFrameworkCore.Examples;

/// <summary>
/// Complete example demonstrating SharpCoreDB with Entity Framework Core.
/// Modern C# 14 patterns: primary constructors, collection expressions, pattern matching.
/// </summary>
public class CompleteExample
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("?? SharpCoreDB + Entity Framework Core Example\n");

        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddDbContext<BlogDbContext>(options =>
            options.UseSharpCoreDB("Data Source=./blog.db;Password=MySecurePassword123;Cache=Shared"));

        var serviceProvider = services.BuildServiceProvider();

        // Run examples
        await RunBasicCrudExample(serviceProvider);
        await RunQueryExample(serviceProvider);
        await RunRelationshipExample(serviceProvider);
        await RunTransactionExample(serviceProvider);

        Console.WriteLine("\n? All examples completed successfully!");
    }

    private static async Task RunBasicCrudExample(IServiceProvider serviceProvider)
    {
        Console.WriteLine("?? Example 1: Basic CRUD Operations\n");

        using var context = serviceProvider.GetRequiredService<BlogDbContext>();
        
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();

        // CREATE
        var blog = new Blog
        {
            Title = "My Tech Blog",
            Url = "https://myblog.com",
            CreatedAt = DateTime.UtcNow
        };

        context.Blogs.Add(blog);
        await context.SaveChangesAsync();
        Console.WriteLine($"? Created blog: {blog.Title} (ID: {blog.BlogId})");

        // READ
        var retrievedBlog = await context.Blogs.FindAsync(blog.BlogId);
        Console.WriteLine($"? Retrieved blog: {retrievedBlog?.Title}");

        // UPDATE
        if (retrievedBlog is not null) // ? C# 14: not pattern
        {
            retrievedBlog.Title = "My Updated Tech Blog";
            await context.SaveChangesAsync();
            Console.WriteLine($"? Updated blog title: {retrievedBlog.Title}");
        }

        // READ ALL
        var allBlogs = await context.Blogs.ToListAsync();
        Console.WriteLine($"? Total blogs: {allBlogs.Count}\n");
    }

    private static async Task RunQueryExample(IServiceProvider serviceProvider)
    {
        Console.WriteLine("?? Example 2: Advanced Queries\n");

        using var context = serviceProvider.GetRequiredService<BlogDbContext>();

        // Add sample data
        var blogs = new List<Blog>
        {
            new() { Title = "C# Programming", Url = "https://csharp.blog", CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new() { Title = ".NET Core Tips", Url = "https://dotnet.blog", CreatedAt = DateTime.UtcNow.AddDays(-20) },
            new() { Title = "Entity Framework Guide", Url = "https://ef.blog", CreatedAt = DateTime.UtcNow.AddDays(-10) }
        };

        context.Blogs.AddRange(blogs);
        await context.SaveChangesAsync();

        // Query with Where
        var recentBlogs = await context.Blogs
            .Where(b => b.CreatedAt > DateTime.UtcNow.AddDays(-15))
            .ToListAsync();
        
        Console.WriteLine($"? Recent blogs (last 15 days): {recentBlogs.Count}");

        // Query with OrderBy and Take
        var topBlogs = await context.Blogs
            .OrderByDescending(b => b.CreatedAt)
            .Take(2)
            .ToListAsync();
        
        Console.WriteLine($"? Top 2 latest blogs:");
        foreach (var blog in topBlogs)
        {
            Console.WriteLine($"  - {blog.Title} ({blog.CreatedAt:yyyy-MM-dd})");
        }

        // Projection (select specific columns)
        var blogTitles = await context.Blogs
            .Select(b => new { b.BlogId, b.Title })
            .ToListAsync();
        
        Console.WriteLine($"? Blog titles count: {blogTitles.Count}");

        // Aggregates
        var count = await context.Blogs.CountAsync();
        Console.WriteLine($"? Total blogs: {count}\n");
    }

    private static async Task RunRelationshipExample(IServiceProvider serviceProvider)
    {
        Console.WriteLine("?? Example 3: Relationships (Blog ? Posts)\n");

        using var context = serviceProvider.GetRequiredService<BlogDbContext>();

        // Create blog with posts
        var blog = new Blog
        {
            Title = "My Awesome Blog",
            Url = "https://awesome.blog",
            CreatedAt = DateTime.UtcNow,
            Posts =
            [
                new Post { Title = "First Post", Content = "Hello World!", PublishedAt = DateTime.UtcNow },
                new Post { Title = "Second Post", Content = "EF Core is great!", PublishedAt = DateTime.UtcNow },
                new Post { Title = "Third Post", Content = "SharpCoreDB rocks!", PublishedAt = DateTime.UtcNow }
            ]
        };

        context.Blogs.Add(blog);
        await context.SaveChangesAsync();
        
        Console.WriteLine($"? Created blog '{blog.Title}' with {blog.Posts.Count} posts");

        // Query with Include (eager loading)
        var blogsWithPosts = await context.Blogs
            .Include(b => b.Posts)
            .ToListAsync();

        foreach (var b in blogsWithPosts)
        {
            Console.WriteLine($"\n? Blog: {b.Title}");
            Console.WriteLine($"  Posts ({b.Posts.Count}):");
            
            foreach (var post in b.Posts)
            {
                Console.WriteLine($"    - {post.Title}");
            }
        }

        Console.WriteLine();
    }

    private static async Task RunTransactionExample(IServiceProvider serviceProvider)
    {
        Console.WriteLine("?? Example 4: Transactions\n");

        using var context = serviceProvider.GetRequiredService<BlogDbContext>();

        // Success scenario
        using (var transaction = await context.Database.BeginTransactionAsync())
        {
            try
            {
                var blog1 = new Blog { Title = "Transaction Blog 1", Url = "https://tx1.blog", CreatedAt = DateTime.UtcNow };
                var blog2 = new Blog { Title = "Transaction Blog 2", Url = "https://tx2.blog", CreatedAt = DateTime.UtcNow };

                context.Blogs.Add(blog1);
                await context.SaveChangesAsync();

                context.Blogs.Add(blog2);
                await context.SaveChangesAsync();

                await transaction.CommitAsync();
                Console.WriteLine("? Transaction committed: 2 blogs created");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"? Transaction rolled back: {ex.Message}");
            }
        }

        // Rollback scenario
        using (var transaction = await context.Database.BeginTransactionAsync())
        {
            try
            {
                var blog = new Blog { Title = "Test Blog", Url = "https://test.blog", CreatedAt = DateTime.UtcNow };
                context.Blogs.Add(blog);
                await context.SaveChangesAsync();

                // Simulate error
                throw new InvalidOperationException("Simulated error");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"? Transaction rolled back successfully: {ex.Message}");
            }
        }

        Console.WriteLine();
    }
}

// ============================================================
// ENTITY MODELS
// ============================================================

/// <summary>
/// Blog entity with one-to-many relationship to Posts.
/// </summary>
public class Blog
{
    [Key]
    public int BlogId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Url { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Navigation property
    public ICollection<Post> Posts { get; set; } = [];
}

/// <summary>
/// Post entity belonging to a Blog.
/// </summary>
public class Post
{
    [Key]
    public int PostId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime PublishedAt { get; set; }

    // Foreign key
    public int BlogId { get; set; }

    // Navigation property
    [ForeignKey(nameof(BlogId))]
    public Blog? Blog { get; set; }
}

// ============================================================
// DB CONTEXT
// ============================================================

/// <summary>
/// Blog database context using SharpCoreDB.
/// Modern C# 14 with primary constructors.
/// </summary>
public class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Blog
        modelBuilder.Entity<Blog>(entity =>
        {
            entity.ToTable("Blogs");
            entity.HasKey(e => e.BlogId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).IsRequired();
            entity.HasIndex(e => e.Url).IsUnique();

            // Relationship
            entity.HasMany(e => e.Posts)
                  .WithOne(p => p.Blog)
                  .HasForeignKey(p => p.BlogId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Post
        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("Posts");
            entity.HasKey(e => e.PostId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Content).IsRequired();
        });
    }
}
