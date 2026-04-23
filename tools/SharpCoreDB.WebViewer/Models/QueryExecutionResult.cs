namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents the outcome of a SQL editor execution request.
/// </summary>
public sealed record class QueryExecutionResult
{
    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<QueryResultRow> Rows { get; init; }

    public int StatementCount { get; init; }

    public int ResultRowCount { get; init; }

    public int NonQueryStatementCount { get; init; }

    public bool Truncated { get; init; }

    public bool SchemaChanged { get; init; }

    public long ExecutionTimeMs { get; init; }

    public required string Summary { get; init; }

    public bool HasRows => Rows.Count > 0;
}

/// <summary>
/// Represents a single result-grid row.
/// </summary>
public sealed record class QueryResultRow
{
    public required IReadOnlyList<string?> Values { get; init; }
}
