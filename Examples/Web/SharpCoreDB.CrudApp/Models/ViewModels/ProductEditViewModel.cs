using System.ComponentModel.DataAnnotations;

namespace SharpCoreDB.CrudApp.Models.ViewModels;

/// <summary>
/// Represents product edit and create form values.
/// </summary>
public sealed record class ProductEditViewModel
{
    /// <summary>Gets or sets product identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets product name.</summary>
    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets product description.</summary>
    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Gets or sets product price.</summary>
    [Range(0.01, 99999999)]
    public decimal Price { get; set; }

    /// <summary>Gets or sets stock quantity.</summary>
    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    /// <summary>Gets or sets category identifier.</summary>
    [Required]
    public Guid CategoryId { get; set; }

    /// <summary>Gets or sets active state.</summary>
    public bool IsActive { get; set; } = true;
}
