namespace SharpCoreDB.CrudApp.Models;

/// <summary>
/// Represents a product category used by the demo CRUD application.
/// </summary>
public sealed record class Category
{
    /// <summary>Gets or sets category identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets whether this category is active.</summary>
    public bool IsActive { get; set; } = true;
}
