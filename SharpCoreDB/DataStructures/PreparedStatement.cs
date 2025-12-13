// <copyright file="PreparedStatement.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a prepared statement for efficient repeated execution.
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
    /// Initializes a new instance of the <see cref="PreparedStatement"/> class.
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <param name="plan">The cached query plan.</param>
    internal PreparedStatement(string sql, CachedQueryPlan plan)
    {
        Sql = sql;
        Plan = plan;
    }
}
