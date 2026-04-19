// <copyright file="SqlParser.GraphRag.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;

/// <summary>
/// GraphRAG execution support for SqlParser.
/// Handles SELECT ... GRAPH_RAG top-level clause execution via optional provider.
/// </summary>
public partial class SqlParser
{
    private IGraphRagProvider? _graphRagProvider;

    /// <summary>
    /// Sets the GRAPH_RAG execution provider for query execution.
    /// </summary>
    /// <param name="provider">GraphRAG provider implementation.</param>
    public void SetGraphRagProvider(IGraphRagProvider provider)
    {
        _graphRagProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    private List<Dictionary<string, object>> ExecuteGraphRagSelect(SelectNode selectNode)
    {
        ArgumentNullException.ThrowIfNull(selectNode);

        if (selectNode.From is null || string.IsNullOrWhiteSpace(selectNode.From.TableName))
        {
            throw new InvalidOperationException("GRAPH_RAG requires a FROM table.");
        }

        var graphRag = selectNode.GraphRag
            ?? throw new InvalidOperationException("GRAPH_RAG clause was not provided.");

        if (_graphRagProvider is null)
        {
            throw new InvalidOperationException("GRAPH_RAG provider is not configured. Register SharpCoreDB.Graph.Advanced SQL integration.");
        }

        if (!_graphRagProvider.CanExecute(selectNode.From.TableName))
        {
            throw new InvalidOperationException($"GRAPH_RAG provider cannot execute for table '{selectNode.From.TableName}'.");
        }

        var request = new GraphRagRequest(
            TableName: selectNode.From.TableName,
            Question: graphRag.Question,
            Columns: selectNode.Columns.Select(c => c.IsWildcard ? "*" : c.Name).ToList(),
            Limit: graphRag.Limit ?? selectNode.Limit ?? 10,
            TopK: graphRag.TopK ?? Math.Max((graphRag.Limit ?? selectNode.Limit ?? 10) * 2, 10),
            MinScore: graphRag.MinScore,
            IncludeContext: graphRag.IncludeContext);

        IReadOnlyList<Dictionary<string, object>> rows;
        try
        {
            rows = _graphRagProvider.ExecuteAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }

        var normalizedRows = rows
            .Select(r => new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return ShapeGraphRagProjection(normalizedRows, selectNode.Columns);
    }

    private static List<Dictionary<string, object>> ShapeGraphRagProjection(
        List<Dictionary<string, object>> rows,
        List<ColumnNode> columns)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0 || columns.Any(c => c.IsWildcard || c.Name == "*"))
        {
            return rows;
        }

        var projected = new List<Dictionary<string, object>>(rows.Count);
        foreach (var row in rows)
        {
            var shaped = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                var sourceKey = ResolveGraphRagSourceKey(column.Name);
                if (!row.TryGetValue(sourceKey, out var value))
                {
                    continue;
                }

                var outputKey = string.IsNullOrWhiteSpace(column.Alias) ? column.Name : column.Alias;
                shaped[outputKey] = value;
            }

            projected.Add(shaped);
        }

        return projected;
    }

    private static string ResolveGraphRagSourceKey(string columnName)
    {
        return columnName.ToUpperInvariant() switch
        {
            "ID" => "node_id",
            "NODEID" => "node_id",
            "SCORE" => "score",
            "CONTEXT" => "context",
            _ => columnName,
        };
    }
}
