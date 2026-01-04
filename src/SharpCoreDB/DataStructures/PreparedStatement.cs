// <copyright file="PreparedStatement.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a prepared statement for efficient repeated execution.
/// ✅ NEW: Now includes compiled query plan for zero-parse SELECT execution.
/// </summary>
public class PreparedStatement
{
    /// <summary>
    /// Gets the original SQL query string.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the cached query execution plan.
    /// </summary>
    public CachedQueryPlan Plan { get; }

    /// <summary>
    /// Gets the compiled query plan (for SELECT queries only).
    /// Returns null for non-SELECT queries or if compilation failed.
    /// </summary>
    public CompiledQueryPlan? CompiledPlan { get; }

    /// <summary>
    /// Gets whether this statement has a compiled query plan.
    /// </summary>
    public bool IsCompiled => CompiledPlan is not null;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreparedStatement"/> class.
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <param name="plan">The cached query plan.</param>
    /// <param name="compiledPlan">The compiled query plan (optional).</param>
    internal PreparedStatement(string sql, CachedQueryPlan plan, CompiledQueryPlan? compiledPlan = null)
    {
        Sql = sql;
        Plan = plan;
        CompiledPlan = compiledPlan;
    }
}
