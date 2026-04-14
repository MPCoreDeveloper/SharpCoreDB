using Microsoft.EntityFrameworkCore;
using SharpCoreDB.CrudApp.Models;

namespace SharpCoreDB.CrudApp.Data;

/// <summary>
/// EF Core context used for schema-friendly registration and relational-style model mapping.
/// </summary>
public sealed class SharpCoreCrudDbContext(DbContextOptions<SharpCoreCrudDbContext> options) : DbContext(options)
{
    /// <summary>Gets products set.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Gets categories set.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Product>(builder =>
        {
            builder.ToTable("Products");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
            builder.Property(x => x.Description).HasMaxLength(2000);
            builder.Property(x => x.Price).HasColumnType("DECIMAL(18,2)");
            builder.Property(x => x.CreatedDate).IsRequired();
            builder.Property(x => x.LastUpdatedDate).IsRequired();
            builder.HasIndex(x => x.Name);
            builder.HasIndex(x => x.CategoryId);
        });

        modelBuilder.Entity<Category>(builder =>
        {
            builder.ToTable("Categories");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
            builder.Property(x => x.Description).HasMaxLength(1000);
            builder.HasIndex(x => x.Name).IsUnique();
        });
    }
}
