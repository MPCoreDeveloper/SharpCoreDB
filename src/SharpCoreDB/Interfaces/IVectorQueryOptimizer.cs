// <copyright file="IVectorQueryOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Optional extension point for vector query optimization.
/// When registered, the query planner detects <c>ORDER BY vec_distance_*(col, query) LIMIT k</c>
/// patterns and routes them to a vector index (HNSW/Flat) instead of performing a full table scan.
/// Zero overhead when not registered — all call sites are guarded by null checks.
/// </summary>
public interface IVectorQueryOptimizer
{
    /// <summary>
    /// Checks whether a vector index exists for the given table and column,
    /// and the specified distance function is supported.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The VECTOR column name.</param>
    /// <param name="distanceFunctionName">The uppercase function name (e.g., "VEC_DISTANCE_COSINE").</param>
    /// <param name="limit">The LIMIT value from the query.</param>
    /// <returns>True if this query can be optimized via a vector index.</returns>
    bool CanOptimize(string tableName, string columnName, string distanceFunctionName, int limit);

    /// <summary>
    /// Executes an optimized vector search using the appropriate index.
    /// Returns pre-sorted top-k row IDs + distances, skipping the full table scan and sort.
    /// </summary>
    /// <param name="table">The table to search.</param>
    /// <param name="tableName">The table name (for index registry lookup).</param>
    /// <param name="vectorColumnName">The VECTOR column name used in the distance function.</param>
    /// <param name="distanceFunctionName">The uppercase function name.</param>
    /// <param name="queryVector">The query vector (float[], byte[], or JSON string).</param>
    /// <param name="limit">Maximum number of results (k).</param>
    /// <param name="noEncrypt">Whether to bypass encryption.</param>
    /// <returns>Pre-sorted results with a "distance" key included, ordered closest first.</returns>
    List<Dictionary<string, object>> ExecuteOptimized(
        ITable table,
        string tableName,
        string vectorColumnName,
        string distanceFunctionName,
        object? queryVector,
        int limit,
        bool noEncrypt);

    /// <summary>
    /// Returns a human-readable explain plan description for EXPLAIN queries.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The VECTOR column name.</param>
    /// <returns>Description like "Vector Index Scan (HNSW, ef=50)" or "Vector Full Scan (Exact)".</returns>
    string GetExplainPlan(string tableName, string columnName);

    /// <summary>
    /// Builds or rebuilds a vector index from the current table data.
    /// Called when CREATE VECTOR INDEX is executed.
    /// </summary>
    /// <param name="table">The table containing vector data.</param>
    /// <param name="tableName">The table name for registry keying.</param>
    /// <param name="columnName">The VECTOR column name.</param>
    /// <param name="indexType">The index type ("FLAT" or "HNSW").</param>
    void BuildIndex(ITable table, string tableName, string columnName, string indexType);

    /// <summary>
    /// Drops a vector index from the registry.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    void DropIndex(string tableName, string columnName);
}
