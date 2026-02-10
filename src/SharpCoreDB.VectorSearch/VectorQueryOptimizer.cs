// <copyright file="VectorQueryOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using SharpCoreDB.Interfaces;

/// <summary>
/// Implements <see cref="IVectorQueryOptimizer"/> by routing eligible queries
/// to live <see cref="IVectorIndex"/> instances managed by <see cref="VectorIndexManager"/>.
/// <para>
/// Detects the pattern: <c>ORDER BY vec_distance_*(col, query) LIMIT k</c>
/// and replaces full table scan + sort with an index-accelerated top-k search.
/// </para>
/// </summary>
public sealed class VectorQueryOptimizer(VectorIndexManager indexManager) : IVectorQueryOptimizer
{
    private static readonly HashSet<string> DistanceFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "VEC_DISTANCE_COSINE",
        "VEC_DISTANCE_L2",
        "VEC_DISTANCE_DOT",
    };

    private readonly VectorIndexManager _indexManager = indexManager ?? throw new ArgumentNullException(nameof(indexManager));

    /// <inheritdoc />
    public bool CanOptimize(string tableName, string columnName, string distanceFunctionName, int limit)
    {
        if (limit <= 0)
            return false;

        if (!DistanceFunctions.Contains(distanceFunctionName))
            return false;

        return _indexManager.HasIndex(tableName, columnName);
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> ExecuteOptimized(
        ITable table,
        string tableName,
        string vectorColumnName,
        string distanceFunctionName,
        object? queryVector,
        int limit,
        bool noEncrypt)
    {
        ArgumentNullException.ThrowIfNull(table);

        var index = _indexManager.GetIndex(tableName, vectorColumnName)
            ?? throw new InvalidOperationException(
                $"No vector index found for {tableName}.{vectorColumnName}. Run CREATE VECTOR INDEX first.");

        // Coerce query vector to float[]
        float[] query = queryVector switch
        {
            float[] arr => arr,
            byte[] bytes => VectorSerializer.Deserialize(bytes),
            string json => VectorSerializer.FromJson(json),
            null => throw new ArgumentException("Query vector cannot be null"),
            _ => throw new ArgumentException($"Unsupported query vector type: {queryVector.GetType().Name}"),
        };

        // Execute index search — returns pre-sorted top-k by distance
        var searchResults = index.Search(query, limit);

        // Fetch full rows from the table and inject distance
        var allRows = table.Select(null, null, true, noEncrypt);
        var results = new List<Dictionary<string, object>>(searchResults.Count);

        foreach (var sr in searchResults)
        {
            if (sr.Id >= 0 && sr.Id < allRows.Count)
            {
                var row = new Dictionary<string, object>(allRows[(int)sr.Id]);
                row["distance"] = sr.Distance;
                results.Add(row);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public string GetExplainPlan(string tableName, string columnName)
    {
        var index = _indexManager.GetIndex(tableName, columnName);
        if (index is null)
            return "Vector Full Scan (no index)";

        return index.IndexType switch
        {
            VectorIndexType.Hnsw => $"Vector Index Scan (HNSW, count={index.Count})",
            VectorIndexType.Flat => $"Vector Index Scan (Flat/Exact, count={index.Count})",
            _ => $"Vector Index Scan ({index.IndexType})",
        };
    }

    /// <inheritdoc />
    public void BuildIndex(ITable table, string tableName, string columnName, string indexType)
    {
        var type = indexType.ToUpperInvariant() switch
        {
            "HNSW" => VectorIndexType.Hnsw,
            "FLAT" => VectorIndexType.Flat,
            _ => VectorIndexType.Flat,
        };

        _indexManager.BuildIndex(table, tableName, columnName, type);
    }

    /// <inheritdoc />
    public void DropIndex(string tableName, string columnName)
    {
        _indexManager.DropIndex(tableName, columnName);
    }
}
