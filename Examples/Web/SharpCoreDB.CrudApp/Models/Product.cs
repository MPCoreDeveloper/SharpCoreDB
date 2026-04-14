namespace SharpCoreDB.CrudApp.Models;

/// <summary>
/// Represents a product managed by the CRUD demo.
/// </summary>
public sealed record class Product
{
    /// <summary>Gets or sets product identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets product name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets product description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets unit price.</summary>
    public decimal Price { get; set; }

    /// <summary>Gets or sets quantity available in stock.</summary>
    public int StockQuantity { get; set; }

    /// <summary>Gets or sets owning category identifier.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Gets or sets whether the product is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets UTC created timestamp.</summary>
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets UTC updated timestamp.</summary>
    public DateTimeOffset LastUpdatedDate { get; set; } = DateTimeOffset.UtcNow;
}
