using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Provides schema metadata for the active web viewer session.
/// </summary>
public interface IMetadataService
{
    /// <summary>
    /// Gets table names for the active database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An ordered table-name list.</returns>
    Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for a selected table.
    /// </summary>
    /// <param name="tableName">Table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Selected table metadata, or <see langword="null"/> when the table is unavailable.</returns>
    Task<TableMetadata?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken = default);
}
