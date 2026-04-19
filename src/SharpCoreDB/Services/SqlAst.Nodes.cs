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
    /// Gets or sets whether SELECT uses OPTIONALLY mode.
    /// When true, result mappers should prefer Option&lt;T&gt; wrappers for projected values.
    /// </summary>
    public bool IsOptionalProjection { get; set; }

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

    /// <summary>
    /// Gets or sets the optional GRAPH_RAG clause.
    /// </summary>
    public GraphRagClauseNode? GraphRag { get; set; }

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

    /// <summary>
    /// Gets or sets the aggregate argument value (e.g., percentile).
    /// </summary>
    public double? AggregateArgument { get; set; }

    /// <summary>
    /// Gets or sets the window function if any (ROW_NUMBER, RANK, etc.).
    /// </summary>
    public string? WindowFunction { get; set; }

    /// <summary>
    /// Gets or sets the PARTITION BY columns for window functions.
    /// </summary>
    public List<string>? WindowPartitionBy { get; set; }

    /// <summary>
    /// Gets or sets the ORDER BY specifications for window functions.
    /// </summary>
    public List<OrderByItem>? WindowOrderBy { get; set; }

    /// <summary>
    /// Gets or sets the FILTER expression for window functions.
    /// </summary>
    public ExpressionNode? WindowFilter { get; set; }

    /// <summary>
    /// Gets or sets the window FRAME clause (e.g., "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW").
    /// </summary>
    public string? WindowFrame { get; set; }

    /// <summary>
    /// Gets or sets a parsed expression for scalar functions (e.g., COALESCE, IIF, NULLIF).
    /// When set, this expression represents the full column value computation.
    /// </summary>
    public ExpressionNode? Expression { get; set; }

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

    /// <summary>
    /// Gets or sets the 1-based ordinal column position (e.g., ORDER BY 2).
    /// Null when ordering by column name.
    /// </summary>
    public int? OrdinalPosition { get; set; }
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

/// <summary>
/// Represents a GRAPH_RAG clause attached to a SELECT statement.
/// </summary>
public class GraphRagClauseNode : SqlNode
{
    /// <summary>
    /// Gets or sets the natural-language question prompt.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional score threshold.
    /// </summary>
    public double? MinScore { get; set; }

    /// <summary>
    /// Gets or sets whether enriched context should be included in result rows.
    /// </summary>
    public bool IncludeContext { get; set; }

    /// <summary>
    /// Gets or sets the optional TOP_K value for candidate retrieval.
    /// </summary>
    public int? TopK { get; set; }

    /// <summary>
    /// Gets or sets the optional limit override scoped to GRAPH_RAG output.
    /// </summary>
    public int? Limit { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor)
        => throw new NotSupportedException("GRAPH_RAG clause does not participate in SQL visitor traversal directly.");
}

/// <summary>
/// Defines the type of set operation between two SELECT arms.
/// </summary>
public enum SetOperationType
{
    /// <summary>UNION — deduplicated.</summary>
    Union,

    /// <summary>UNION ALL — no deduplication.</summary>
    UnionAll,

    /// <summary>INTERSECT — rows present in both arms.</summary>
    Intersect,

    /// <summary>EXCEPT — rows in left arm but not in right arm.</summary>
    Except,
}

/// <summary>
/// Represents a set operation (UNION / UNION ALL / INTERSECT / EXCEPT) combining two SELECT arms.
/// The outer ORDER BY and LIMIT/OFFSET apply to the combined result.
/// </summary>
public class SetOperationNode : SqlNode
{
    /// <summary>
    /// Gets or sets the left SELECT arm.
    /// </summary>
    public required SelectNode Left { get; set; }

    /// <summary>
    /// Gets or sets the right SELECT arm.
    /// </summary>
    public required SelectNode Right { get; set; }

    /// <summary>
    /// Gets or sets the set operation type.
    /// </summary>
    public SetOperationType Operation { get; set; }

    /// <summary>
    /// Gets or sets the optional outer ORDER BY clause applied to the combined result.
    /// </summary>
    public OrderByNode? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets the optional outer LIMIT.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets or sets the optional outer OFFSET.
    /// </summary>
    public int? Offset { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitSetOperation(this);
}

/// <summary>
/// Represents a WITH RECURSIVE CTE (Common Table Expression).
/// </summary>
public class WithRecursiveNode : SqlNode
{
    /// <summary>
    /// Gets or sets the CTE name.
    /// </summary>
    public string CteName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional CTE column list.
    /// </summary>
    public List<string> ColumnNames { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this is a RECURSIVE CTE.
    /// </summary>
    public bool IsRecursive { get; set; }

    /// <summary>
    /// Gets or sets the anchor (base case) SELECT.
    /// </summary>
    public required SelectNode AnchorSelect { get; set; }

    /// <summary>
    /// Gets or sets the recursive SELECT arm (after UNION ALL).
    /// </summary>
    public SelectNode? RecursiveSelect { get; set; }

    /// <summary>
    /// Gets or sets the outer query that references the CTE.
    /// </summary>
    public required SqlNode OuterQuery { get; set; }

    /// <summary>
    /// Gets or sets the maximum iteration limit for cycle detection.
    /// </summary>
    public int MaxIterations { get; set; } = 1000;

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitSelect(
        (SelectNode)OuterQuery); // Delegate to outer query execution
}

/// <summary>
/// Represents an EXPLAIN QUERY PLAN statement.
/// </summary>
public class ExplainQueryPlanNode : SqlNode
{
    /// <summary>
    /// Gets or sets the inner statement being explained.
    /// </summary>
    public required SqlNode InnerStatement { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) =>
        throw new NotSupportedException("EXPLAIN QUERY PLAN is handled directly by the executor.");
}

/// <summary>
/// Represents a BEGIN TRANSACTION statement.
/// </summary>
public class BeginTransactionNode : SqlNode
{
    /// <summary>
    /// Gets or sets the transaction mode (DEFERRED, IMMEDIATE, EXCLUSIVE).
    /// </summary>
    public string Mode { get; set; } = "DEFERRED";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) =>
        throw new NotSupportedException("BEGIN TRANSACTION is handled directly by the executor.");
}

/// <summary>
/// Represents a SAVEPOINT statement.
/// </summary>
public class SavepointNode : SqlNode
{
    /// <summary>
    /// Gets or sets the savepoint name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the savepoint action (Save, Release, RollbackTo).
    /// </summary>
    public SavepointAction Action { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) =>
        throw new NotSupportedException("SAVEPOINT is handled directly by the executor.");
}

/// <summary>
/// Savepoint action types.
/// </summary>
public enum SavepointAction
{
    /// <summary>Create a savepoint.</summary>
    Save,

    /// <summary>Release (commit) a savepoint.</summary>
    Release,

    /// <summary>Rollback to a savepoint.</summary>
    RollbackTo,
}
