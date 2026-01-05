// <copyright file="CompiledQueryPlan.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Optimization;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

/// <summary>
/// Represents a compiled query execution plan with cached expression trees.
/// Eliminates parsing overhead for repeated SELECT statements.
/// Expected performance: 5-10x faster than re-parsing for repeated queries.
/// </summary>
public class CompiledQueryPlan
{
    /// <summary>
    /// Gets the original SQL statement.
    /// </summary>
    public string Sql { get; }

    /// <summary>
    /// Gets the table name being queried.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the list of columns to select.
    /// </summary>
    public List<string> SelectColumns { get; }

    /// <summary>
    /// Gets whether this is a SELECT * query.
    /// </summary>
    public bool IsSelectAll { get; }

    /// <summary>
    /// Gets the compiled WHERE clause filter delegate.
    /// Returns null if no WHERE clause exists.
    /// </summary>
    public Func<Dictionary<string, object>, bool>? WhereFilter { get; }

    /// <summary>
    /// Gets the compiled projection delegate.
    /// Projects full row to selected columns only.
    /// </summary>
    public Func<Dictionary<string, object>, Dictionary<string, object>>? ProjectionFunc { get; }

    /// <summary>
    /// Gets the ORDER BY column name.
    /// </summary>
    public string? OrderByColumn { get; }

    /// <summary>
    /// Gets whether the ORDER BY is ascending.
    /// </summary>
    public bool OrderByAscending { get; }

    /// <summary>
    /// Gets the LIMIT value.
    /// </summary>
    public int? Limit { get; }

    /// <summary>
    /// Gets the OFFSET value.
    /// </summary>
    public int? Offset { get; }

    /// <summary>
    /// Gets the parameter names used in the query.
    /// </summary>
    public HashSet<string> ParameterNames { get; }

    /// <summary>
    /// Gets whether this query has a WHERE clause.
    /// </summary>
    public bool HasWhereClause => WhereFilter is not null;

    /// <summary>
    /// Gets whether this query has projections.
    /// </summary>
    public bool HasProjection => ProjectionFunc is not null;

    /// <summary>
    /// Gets the optimizer-produced physical plan (if available).
    /// </summary>
    public PhysicalPlan? OptimizedPlan { get; }

    /// <summary>
    /// Gets the estimated cost for the optimized plan.
    /// </summary>
    public double OptimizedCost { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledQueryPlan"/> class.
    /// </summary>
    /// <param name="sql">The original SQL statement.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="selectColumns">The columns to select.</param>
    /// <param name="isSelectAll">Whether this is SELECT *.</param>
    /// <param name="whereFilter">The compiled WHERE filter.</param>
    /// <param name="projectionFunc">The compiled projection function.</param>
    /// <param name="orderByColumn">The ORDER BY column.</param>
    /// <param name="orderByAscending">Whether ORDER BY is ascending.</param>
    /// <param name="limit">The LIMIT value.</param>
    /// <param name="offset">The OFFSET value.</param>
    /// <param name="parameterNames">The parameter names.</param>
    /// <param name="optimizedPlan">Physical plan produced by optimizer (optional).</param>
    /// <param name="optimizedCost">Estimated cost for optimized plan.</param>
    public CompiledQueryPlan(
        string sql,
        string tableName,
        List<string> selectColumns,
        bool isSelectAll,
        Func<Dictionary<string, object>, bool>? whereFilter,
        Func<Dictionary<string, object>, Dictionary<string, object>>? projectionFunc,
        string? orderByColumn,
        bool orderByAscending,
        int? limit,
        int? offset,
        HashSet<string> parameterNames,
        PhysicalPlan? optimizedPlan = null,
        double optimizedCost = 0d)
    {
        Sql = sql;
        TableName = tableName;
        SelectColumns = selectColumns;
        IsSelectAll = isSelectAll;
        WhereFilter = whereFilter;
        ProjectionFunc = projectionFunc;
        OrderByColumn = orderByColumn;
        OrderByAscending = orderByAscending;
        Limit = limit;
        Offset = offset;
        ParameterNames = parameterNames;
        OptimizedPlan = optimizedPlan;
        OptimizedCost = optimizedCost;
    }
}
