namespace SharpCoreDB.CrudApp.Models.ViewModels;

/// <summary>
/// Represents product details and related category name.
/// </summary>
public sealed record class ProductDetailsViewModel(Product Product, string CategoryName);
