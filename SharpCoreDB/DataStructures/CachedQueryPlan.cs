// <copyright file="CachedQueryPlan.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a cached query execution plan for prepared statements.
/// </summary>
public record CachedQueryPlan(string Sql, string[] Parts);
