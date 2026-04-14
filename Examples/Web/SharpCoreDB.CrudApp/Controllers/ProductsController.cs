using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SharpCoreDB.CrudApp.Models.ViewModels;
using SharpCoreDB.CrudApp.Services;

namespace SharpCoreDB.CrudApp.Controllers;

/// <summary>
/// Provides full CRUD operations for products.
/// </summary>
[Authorize]
public sealed class ProductsController(ProductCrudService productService) : Controller
{
    private readonly ProductCrudService _productService = productService ?? throw new ArgumentNullException(nameof(productService));

    /// <summary>
    /// Lists all products.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var products = await _productService.GetProductsAsync(cancellationToken).ConfigureAwait(false);
        return View(products);
    }

    /// <summary>
    /// Shows product details.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return NotFound();
        }

        var categoryName = await _productService.GetCategoryNameAsync(product.CategoryId, cancellationToken).ConfigureAwait(false);
        return View(new ProductDetailsViewModel(product, categoryName));
    }

    /// <summary>
    /// Shows product create form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateCategoriesAsync(Guid.Empty, cancellationToken).ConfigureAwait(false);
        return View(new ProductEditViewModel());
    }

    /// <summary>
    /// Creates a product.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(model.CategoryId, cancellationToken).ConfigureAwait(false);
            return View(model);
        }

        await _productService.CreateProductAsync(model, cancellationToken).ConfigureAwait(false);
        TempData["SuccessMessage"] = "Product created successfully.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Shows edit form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return NotFound();
        }

        var model = new ProductEditViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            CategoryId = product.CategoryId,
            IsActive = product.IsActive
        };

        await PopulateCategoriesAsync(model.CategoryId, cancellationToken).ConfigureAwait(false);
        return View(model);
    }

    /// <summary>
    /// Updates a product.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ProductEditViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateCategoriesAsync(model.CategoryId, cancellationToken).ConfigureAwait(false);
            return View(model);
        }

        var updated = await _productService.UpdateProductAsync(model, cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Product updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Shows delete confirmation.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return NotFound();
        }

        return View(product);
    }

    /// <summary>
    /// Deletes a product.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _productService.DeleteProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = "Product deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCategoriesAsync(Guid selectedCategoryId, CancellationToken cancellationToken)
    {
        var categories = await _productService.GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
        ViewBag.Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString("D"), c.Id == selectedCategoryId)).ToList();
    }
}
