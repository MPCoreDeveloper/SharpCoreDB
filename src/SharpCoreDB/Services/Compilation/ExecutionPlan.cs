// <copyright file="ExecutionPlan.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services.Compilation;

using System;
using System.Collections.Generic;

/// <summary>
/// ✅ VALUE TYPE: Stack-allocatable query execution plan.
/// No heap allocation, reusable across parameter bindings.
/// Replaces CompiledQueryPlan for high-performance scenarios.
/// </summary>
public readonly struct ExecutionPlan
{
    /// <summary>Gets the original SQL statement (for reference/caching).</summary>
    public string Sql { get; init; }

    /// <summary>Gets the table name being queried.</summary>
    public string TableName { get; init; }

    /// <summary>Gets the selected column names (empty = SELECT *).</summary>
    public IReadOnlyList<string> SelectColumns { get; init; }

    /// <summary>Gets whether this is SELECT * (no projection).</summary>
    public bool IsSelectAll { get; init; }

    /// <summary>
    /// Gets the compiled WHERE clause filter.
    /// Takes (row, parameters) and returns bool for filtering.
    /// Null if no WHERE clause.
    /// </summary>
    public Func<Dictionary<string, object>, Dictionary<string, object?>?, bool>? WhereFilter { get; init; }

    /// <summary>
    /// Gets the projection delegate to select specific columns.
    /// Takes full row and returns projected row.
    /// Null if SELECT * or no projection needed.
    /// </summary>
    public Func<Dictionary<string, object>, Dictionary<string, object>>? ProjectionFunc { get; init; }

    /// <summary>Gets the ORDER BY column name.</summary>
    public string? OrderByColumn { get; init; }

    /// <summary>Gets whether ORDER BY is ascending (vs descending).</summary>
    public bool OrderByAscending { get; init; }

    /// <summary>Gets the LIMIT count (null = no limit).</summary>
    public int? Limit { get; init; }

    /// <summary>Gets the OFFSET count (null = no offset).</summary>
    public int? Offset { get; init; }

    /// <summary>Gets parameter names used in the WHERE clause.</summary>
    public IReadOnlySet<string> ParameterNames { get; init; }

    /// <summary>Gets whether this plan has a WHERE clause.</summary>
    public bool HasWhereClause => WhereFilter is not null;

    /// <summary>Gets whether this plan requires projection.</summary>
    public bool HasProjection => ProjectionFunc is not null;

    /// <summary>Gets whether this plan requires ordering.</summary>
    public bool HasOrdering => !string.IsNullOrEmpty(OrderByColumn);

    /// <summary>Gets whether this plan requires limiting.</summary>
    public bool HasLimit => Limit.HasValue;

    /// <summary>Gets whether this plan requires offset.</summary>
    public bool HasOffset => Offset.HasValue;

    /// <summary>
    /// Gets total SQL clauses in this plan (rough complexity measure).
    /// </summary>
    public int Complexity
    {
        get
        {
            var count = 0;
            if (HasWhereClause) count++;
            if (HasOrdering) count++;
            if (HasLimit) count++;
            if (HasOffset) count++;
            if (HasProjection) count++;
            return count;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionPlan"/> struct.
    /// </summary>
    public ExecutionPlan(
        string sql,
        string tableName,
        IReadOnlyList<string> selectColumns,
        bool isSelectAll,
        Func<Dictionary<string, object>, Dictionary<string, object?>?, bool>? whereFilter,
        Func<Dictionary<string, object>, Dictionary<string, object>>? projectionFunc,
        string? orderByColumn,
        bool orderByAscending,
        int? limit,
        int? offset,
        IReadOnlySet<string> parameterNames)
    {
        Sql = sql;
        TableName = tableName;
        SelectColumns = selectColumns ?? Array.Empty<string>();
        IsSelectAll = isSelectAll;
        WhereFilter = whereFilter;
        ProjectionFunc = projectionFunc;
        OrderByColumn = orderByColumn;
        OrderByAscending = orderByAscending;
        Limit = limit;
        Offset = offset;
        ParameterNames = parameterNames ?? new HashSet<string>();
    }

    /// <summary>
    /// Returns the number of output columns this plan will produce.
    /// </summary>
    public int GetOutputColumnCount() => IsSelectAll ? -1 : SelectColumns.Count;
}
