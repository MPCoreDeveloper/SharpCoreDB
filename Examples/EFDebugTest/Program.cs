using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var dbPath = "./test_debug.scdb";
if (File.Exists(dbPath)) File.Delete(dbPath);

var services = new ServiceCollection();
services.AddDbContext<TestDbContext>(options =>
    options.UseSharpCoreDB($"Data Source={dbPath};Password=Test123;Cache=Shared"));

var provider = services.BuildServiceProvider();
using var context = provider.GetRequiredService<TestDbContext>();

await context.Database.EnsureCreatedAsync();
context.Blogs.Add(new TestBlog { Title = "Test", Url = "https://test.com", CreatedAt = DateTime.UtcNow });
await context.SaveChangesAsync();

Console.WriteLine("✓ Blog inserted");

// This is the failing query
var titles = await context.Blogs
    .Select(b => new { b.BlogId, b.Title })
    .ToListAsync();

Console.WriteLine($"✓ Query returned {titles.Count} items");
foreach (var item in titles)
{
    Console.WriteLine($"  BlogId={item.BlogId}, Title={item.Title}");
}

public class TestBlog
{
    [Key]
    public int BlogId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Url { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestBlog> Blogs => Set<TestBlog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestBlog>(entity =>
        {
            entity.ToTable("Blogs");
            entity.HasKey(e => e.BlogId);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).IsRequired();
        });
    }
}
