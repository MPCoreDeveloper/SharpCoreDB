// <copyright file="SqlAst.Nodes.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// SqlAst - Query node types.
/// Contains SELECT, FROM, JOIN, WHERE, ORDER BY, GROUP BY, HAVING and expression nodes.
/// Part of the SqlAst partial class infrastructure.
/// Modern C# 14 with collection expressions and target-typed new.
/// See also: SqlAst.Core.cs, SqlAst.DML.cs
/// </summary>
public static partial class SqlAst
{
    // Marker for Nodes partial
}

/// <summary>
/// Represents a SELECT statement.
/// ✅ C# 14: Collection expressions for list initialization.
/// </summary>
public class SelectNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of selected columns.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ColumnNode> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the FROM clause.
    /// </summary>
    public FromNode? From { get; set; }

    /// <summary>
    /// Gets or sets the WHERE clause.
    /// </summary>
    public WhereNode? Where { get; set; }

    /// <summary>
    /// Gets or sets the ORDER BY clause.
    /// </summary>
    public OrderByNode? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets the LIMIT value.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets or sets the OFFSET value.
    /// </summary>
    public int? Offset { get; set; }

    /// <summary>
    /// Gets or sets whether this is a DISTINCT query.
    /// </summary>
    public bool IsDistinct { get; set; }

    /// <summary>
    /// Gets or sets the GROUP BY clause.
    /// </summary>
    public GroupByNode? GroupBy { get; set; }

    /// <summary>
    /// Gets or sets the HAVING clause.
    /// </summary>
    public HavingNode? Having { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitSelect(this);
}

/// <summary>
/// Represents a column in the SELECT list.
/// </summary>
public class ColumnNode : SqlNode
{
    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the table alias.
    /// </summary>
    public string? TableAlias { get; set; }

    /// <summary>
    /// Gets or sets the column alias.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Gets or sets whether this is a wildcard (*).
    /// </summary>
    public bool IsWildcard { get; set; }

    /// <summary>
    /// Gets or sets the aggregate function if any.
    /// </summary>
    public string? AggregateFunction { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitColumn(this);
}

/// <summary>
/// Represents a FROM clause with optional JOINs.
/// ✅ C# 14: Collection expression.
/// </summary>
public class FromNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the table alias.
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>
    /// Gets or sets the list of JOINs.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<JoinNode> Joins { get; set; } = [];

    /// <summary>
    /// Gets or sets a subquery if this is a derived table.
    /// </summary>
    public SelectNode? Subquery { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitFrom(this);
}

/// <summary>
/// Represents a JOIN clause.
/// ✅ C# 14: Target-typed new for default instance.
/// </summary>
public class JoinNode : SqlNode
{
    /// <summary>
    /// JOIN type enumeration.
    /// </summary>
    public enum JoinType
    {
        /// <summary>INNER JOIN.</summary>
        Inner,

        /// <summary>LEFT OUTER JOIN.</summary>
        Left,

        /// <summary>RIGHT OUTER JOIN.</summary>
        Right,

        /// <summary>FULL OUTER JOIN.</summary>
        Full,

        /// <summary>CROSS JOIN.</summary>
        Cross
    }

    /// <summary>
    /// Gets or sets the join type.
    /// </summary>
    public JoinType Type { get; set; }

    /// <summary>
    /// Gets or sets the table to join.
    /// ✅ C# 14: Target-typed new.
    /// </summary>
    public FromNode Table { get; set; } = new();

    /// <summary>
    /// Gets or sets the ON condition.
    /// </summary>
    public ExpressionNode? OnCondition { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitJoin(this);
}

/// <summary>
/// Represents a WHERE clause.
/// ✅ C# 14: Target-typed new.
/// </summary>
public class WhereNode : SqlNode
{
    /// <summary>
    /// Gets or sets the condition expression.
    /// ✅ C# 14: Target-typed new.
    /// </summary>
    public ExpressionNode Condition { get; set; } = new BinaryExpressionNode();

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitWhere(this);
}

/// <summary>
/// Represents a binary expression (e.g., a = b, a AND b).
/// </summary>
public class BinaryExpressionNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the left operand.
    /// </summary>
    public ExpressionNode? Left { get; set; }

    /// <summary>
    /// Gets or sets the operator.
    /// </summary>
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the right operand.
    /// </summary>
    public ExpressionNode? Right { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitBinaryExpression(this);
}

/// <summary>
/// Represents a literal value.
/// </summary>
public class LiteralNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the literal value.
    /// </summary>
    public object? Value { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitLiteral(this);
}

/// <summary>
/// Represents a column reference in an expression.
/// </summary>
public class ColumnReferenceNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the table alias.
    /// </summary>
    public string? TableAlias { get; set; }

    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitColumnReference(this);
}

/// <summary>
/// Represents an IN expression.
/// ✅ C# 14: Collection expression.
/// </summary>
public class InExpressionNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the expression to test.
    /// </summary>
    public ExpressionNode? Expression { get; set; }

    /// <summary>
    /// Gets or sets the list of values.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ExpressionNode> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this is a NOT IN expression.
    /// </summary>
    public bool IsNot { get; set; }

    /// <summary>
    /// Gets or sets a subquery for IN (SELECT ...).
    /// </summary>
    public SelectNode? Subquery { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitInExpression(this);
}

/// <summary>
/// Represents an ORDER BY clause.
/// ✅ C# 14: Collection expression.
/// </summary>
public class OrderByNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of order by items.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<OrderByItem> Items { get; set; } = [];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitOrderBy(this);
}

/// <summary>
/// Represents an item in the ORDER BY clause.
/// ✅ C# 14: Target-typed new.
/// </summary>
public class OrderByItem
{
    /// <summary>
    /// Gets or sets the column reference.
    /// ✅ C# 14: Target-typed new.
    /// </summary>
    public ColumnReferenceNode Column { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this is ascending (default true).
    /// </summary>
    public bool IsAscending { get; set; } = true;
}

/// <summary>
/// Represents a GROUP BY clause.
/// ✅ C# 14: Collection expression.
/// </summary>
public class GroupByNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of grouping columns.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ColumnReferenceNode> Columns { get; set; } = [];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitGroupBy(this);
}

/// <summary>
/// Represents a HAVING clause.
/// ✅ C# 14: Target-typed new.
/// </summary>
public class HavingNode : SqlNode
{
    /// <summary>
    /// Gets or sets the condition expression.
    /// ✅ C# 14: Target-typed new.
    /// </summary>
    public ExpressionNode Condition { get; set; } = new BinaryExpressionNode();

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitHaving(this);
}

/// <summary>
/// Represents a function call.
/// ✅ C# 14: Collection expression.
/// </summary>
public class FunctionCallNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the function name.
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the function arguments.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ExpressionNode> Arguments { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this is a DISTINCT aggregate.
    /// </summary>
    public bool IsDistinct { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitFunctionCall(this);
}

/// <summary>
/// Represents a GRAPH_TRAVERSE expression for index-free graph traversal.
/// ✅ GraphRAG Phase 1: Core graph traversal support via ROWREF adjacency.
/// </summary>
public class GraphTraverseNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the table name or expression to traverse.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the starting node ID expression.
    /// </summary>
    public ExpressionNode? StartNode { get; set; }

    /// <summary>
    /// Gets or sets the ROWREF relationship column name.
    /// </summary>
    public string RelationshipColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum traversal depth expression.
    /// </summary>
    public ExpressionNode? MaxDepth { get; set; }

    /// <summary>
    /// Gets or sets the optional traversal strategy (BFS/DFS).
    /// Default is BFS if not specified.
    /// </summary>
    public string Strategy { get; set; } = "BFS";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitGraphTraverse(this);
}
