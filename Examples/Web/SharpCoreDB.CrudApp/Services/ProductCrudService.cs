using System.Globalization;
using SharpCoreDB.CrudApp.Models;
using SharpCoreDB.CrudApp.Models.ViewModels;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.CrudApp.Services;

/// <summary>
/// Provides product and category CRUD operations on top of SharpCoreDB SQL execution.
/// </summary>
public sealed class ProductCrudService(IDatabase database)
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// Gets all products ordered by name.
    /// </summary>
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _database.ExecuteQuery("SELECT * FROM Products ORDER BY Name");
        return Task.FromResult<IReadOnlyList<Product>>(rows.Select(MapProduct).ToArray());
    }

    /// <summary>
    /// Gets a single product by identifier.
    /// </summary>
    public Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _database.ExecuteQuery($"SELECT * FROM Products WHERE Id = '{id:D}'");
        return Task.FromResult(rows.Count == 0 ? null : MapProduct(rows[0]));
    }

    /// <summary>
    /// Gets all active categories ordered by name.
    /// </summary>
    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _database.ExecuteQuery("SELECT * FROM Categories WHERE IsActive = 1 ORDER BY Name");
        return Task.FromResult<IReadOnlyList<Category>>(rows.Select(MapCategory).ToArray());
    }

    /// <summary>
    /// Gets category display name for a category id.
    /// </summary>
    public Task<string> GetCategoryNameAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rows = _database.ExecuteQuery($"SELECT Name FROM Categories WHERE Id = '{categoryId:D}'");
        return Task.FromResult(rows.Count == 0 ? "Unknown" : Convert.ToString(rows[0]["Name"], CultureInfo.InvariantCulture) ?? "Unknown");
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    public async Task CreateProductAsync(ProductEditViewModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
        var now = DateTimeOffset.UtcNow;

        var statement = $"INSERT INTO Products (Id, Name, Description, Price, StockQuantity, CategoryId, IsActive, CreatedDate, LastUpdatedDate) VALUES ('{id:D}', '{EscapeSql(model.Name)}', {SqlNullable(model.Description)}, {model.Price.ToString(CultureInfo.InvariantCulture)}, {model.StockQuantity}, '{model.CategoryId:D}', {(model.IsActive ? 1 : 0)}, '{now:O}', '{now:O}')";

        await _database.ExecuteBatchSQLAsync([statement], cancellationToken).ConfigureAwait(false);
        _database.Flush();
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    public async Task<bool> UpdateProductAsync(ProductEditViewModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetProductAsync(model.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var statement = $"UPDATE Products SET Name = '{EscapeSql(model.Name)}', Description = {SqlNullable(model.Description)}, Price = {model.Price.ToString(CultureInfo.InvariantCulture)}, StockQuantity = {model.StockQuantity}, CategoryId = '{model.CategoryId:D}', IsActive = {(model.IsActive ? 1 : 0)}, LastUpdatedDate = '{now:O}' WHERE Id = '{model.Id:D}'";

        await _database.ExecuteBatchSQLAsync([statement], cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return true;
    }

    /// <summary>
    /// Deletes a product.
    /// </summary>
    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await GetProductAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        var statement = $"DELETE FROM Products WHERE Id = '{id:D}'";
        await _database.ExecuteBatchSQLAsync([statement], cancellationToken).ConfigureAwait(false);
        _database.Flush();
        return true;
    }

    private static Product MapProduct(IReadOnlyDictionary<string, object> row)
    {
        return new Product
        {
            Id = Guid.Parse(Convert.ToString(row["Id"], CultureInfo.InvariantCulture)!),
            Name = Convert.ToString(row["Name"], CultureInfo.InvariantCulture) ?? string.Empty,
            Description = row.TryGetValue("Description", out var description) && description is not DBNull
                ? Convert.ToString(description, CultureInfo.InvariantCulture)
                : null,
            Price = Convert.ToDecimal(row["Price"], CultureInfo.InvariantCulture),
            StockQuantity = Convert.ToInt32(row["StockQuantity"], CultureInfo.InvariantCulture),
            CategoryId = Guid.Parse(Convert.ToString(row["CategoryId"], CultureInfo.InvariantCulture)!),
            IsActive = Convert.ToInt32(row["IsActive"], CultureInfo.InvariantCulture) == 1,
            CreatedDate = DateTimeOffset.Parse(Convert.ToString(row["CreatedDate"], CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
            LastUpdatedDate = DateTimeOffset.Parse(Convert.ToString(row["LastUpdatedDate"], CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)
        };
    }

    private static Category MapCategory(IReadOnlyDictionary<string, object> row)
    {
        return new Category
        {
            Id = Guid.Parse(Convert.ToString(row["Id"], CultureInfo.InvariantCulture)!),
            Name = Convert.ToString(row["Name"], CultureInfo.InvariantCulture) ?? string.Empty,
            Description = row.TryGetValue("Description", out var description) && description is not DBNull
                ? Convert.ToString(description, CultureInfo.InvariantCulture)
                : null,
            IsActive = Convert.ToInt32(row["IsActive"], CultureInfo.InvariantCulture) == 1
        };
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string SqlNullable(string? value) => value is null ? "NULL" : $"'{EscapeSql(value)}'";
}
