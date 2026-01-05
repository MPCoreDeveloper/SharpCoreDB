// <copyright file="SubqueryNode.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// Represents a subquery expression in the AST.
/// HOT PATH - Zero-allocation, supports streaming execution.
/// ✅ C# 14: Collection expressions, required properties, is patterns.
/// 
/// Subquery Types:
/// - Scalar: Returns single value (e.g., (SELECT MAX(price) FROM products))
/// - Row: Returns single row with multiple columns
/// - Table: Returns multiple rows (e.g., IN (SELECT ...), FROM (SELECT ...))
/// 
/// Correlation:
/// - Non-correlated: Independent of outer query, cacheable
/// - Correlated: References outer query columns, evaluated per outer row
/// 
/// Performance:
/// - Non-correlated: O(1) after first evaluation (cached)
/// - Correlated: O(n) where n = outer rows, optimized with join conversion
/// </summary>
public sealed class SubqueryExpressionNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the SELECT statement representing the subquery.
    /// ✅ C# 14: Required property.
    /// </summary>
    public required SelectNode Query { get; init; }

    /// <summary>
    /// Gets or sets the subquery type classification.
    /// ✅ C# 14: Default value via property initializer.
    /// </summary>
    public SubqueryType Type { get; set; } = SubqueryType.Unknown;

    /// <summary>
    /// Gets or sets whether this subquery is correlated.
    /// Correlated subqueries reference columns from outer query.
    /// </summary>
    public bool IsCorrelated { get; set; }

    /// <summary>
    /// Gets or sets the list of outer column references.
    /// Only populated for correlated subqueries.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ColumnReferenceNode> OuterReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets the correlation depth (nesting level).
    /// 0 = non-correlated, 1 = immediate parent, 2+ = multiple levels.
    /// </summary>
    public int CorrelationDepth { get; set; }

    /// <summary>
    /// Gets or sets whether this subquery can be cached.
    /// Non-correlated subqueries are cacheable.
    /// </summary>
    public bool IsCacheable => !IsCorrelated;

    /// <summary>
    /// Gets or sets the cache key for non-correlated subqueries.
    /// </summary>
    public string? CacheKey { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor)
    {
        // Subquery nodes can be visited as SelectNodes
        return Query.Accept(visitor);
    }
}

/// <summary>
/// Classification of subquery result types.
/// </summary>
public enum SubqueryType
{
    /// <summary>Type not yet determined.</summary>
    Unknown,

    /// <summary>
    /// Scalar subquery - returns single value.
    /// Example: SELECT (SELECT MAX(price) FROM products) as max_price
    /// </summary>
    Scalar,

    /// <summary>
    /// Row subquery - returns single row with multiple columns.
    /// Example: WHERE (col1, col2) = (SELECT a, b FROM t WHERE ...)
    /// </summary>
    Row,

    /// <summary>
    /// Table subquery - returns multiple rows.
    /// Example: SELECT * FROM (SELECT * FROM users WHERE active = 1) u
    /// Example: WHERE id IN (SELECT user_id FROM orders)
    /// </summary>
    Table
}
