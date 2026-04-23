using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Executes SQL editor requests for the active viewer session.
/// </summary>
public interface IViewerQueryService
{
    /// <summary>
    /// Executes one or more SQL statements for the active session.
    /// </summary>
    /// <param name="request">SQL execution input.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result for the last result set and overall run summary.</returns>
    Task<QueryExecutionResult> ExecuteAsync(QueryExecutionRequest request, CancellationToken cancellationToken = default);
}
