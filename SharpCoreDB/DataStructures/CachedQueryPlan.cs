// <copyright file="CachedQueryPlan.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a cached query execution plan for prepared statements.
/// </summary>
public class CachedQueryPlan
{
    /// <summary>
    /// Gets or sets the original SQL query string.
    /// </summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the parsed parts of the SQL query.
    /// </summary>
    public string[] Parts { get; set; } = [];
}
