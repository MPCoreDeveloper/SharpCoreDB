// <copyright file="SqlAst.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// Visitor interface for SQL AST nodes.
/// </summary>
public interface ISqlVisitor
{
    /// <summary>Visits a SELECT node.</summary>
    object? VisitSelect(SelectNode node);

    /// <summary>Visits a column node.</summary>
    object? VisitColumn(ColumnNode node);

    /// <summary>Visits a FROM node.</summary>
    object? VisitFrom(FromNode node);

    /// <summary>Visits a JOIN node.</summary>
    object? VisitJoin(JoinNode node);

    /// <summary>Visits a WHERE node.</summary>
    object? VisitWhere(WhereNode node);

    /// <summary>Visits a binary expression node.</summary>
    object? VisitBinaryExpression(BinaryExpressionNode node);

    /// <summary>Visits a literal node.</summary>
    object? VisitLiteral(LiteralNode node);

    /// <summary>Visits a column reference node.</summary>
    object? VisitColumnReference(ColumnReferenceNode node);

    /// <summary>Visits an IN expression node.</summary>
    object? VisitInExpression(InExpressionNode node);

    /// <summary>Visits an ORDER BY node.</summary>
    object? VisitOrderBy(OrderByNode node);

    /// <summary>Visits a GROUP BY node.</summary>
    object? VisitGroupBy(GroupByNode node);

    /// <summary>Visits a HAVING node.</summary>
    object? VisitHaving(HavingNode node);

    /// <summary>Visits a function call node.</summary>
    object? VisitFunctionCall(FunctionCallNode node);

    /// <summary>Visits an INSERT node.</summary>
    object? VisitInsert(InsertNode node);

    /// <summary>Visits an UPDATE node.</summary>
    object? VisitUpdate(UpdateNode node);

    /// <summary>Visits a DELETE node.</summary>
    object? VisitDelete(DeleteNode node);

    /// <summary>Visits a CREATE TABLE node.</summary>
    object? VisitCreateTable(CreateTableNode node);
}

/// <summary>
/// Abstract base class for SQL AST nodes.
/// </summary>
public abstract class SqlNode
{
    /// <summary>
    /// Gets or sets the source position for error reporting.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Accepts a visitor for the visitor pattern.
    /// </summary>
    /// <param name="visitor">The visitor instance.</param>
    /// <returns>Result of the visit.</returns>
    public abstract object? Accept(ISqlVisitor visitor);
}

/// <summary>
/// Represents a SELECT statement.
/// </summary>
public class SelectNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of selected columns.
    /// </summary>
    public List<ColumnNode> Columns { get; set; } = new();

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
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitSelect(this);
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
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitColumn(this);
}

/// <summary>
/// Represents a FROM clause with optional JOINs.
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
    /// </summary>
    public List<JoinNode> Joins { get; set; } = new();

    /// <summary>
    /// Gets or sets a subquery if this is a derived table.
    /// </summary>
    public SelectNode? Subquery { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitFrom(this);
}

/// <summary>
/// Represents a JOIN clause.
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
    /// </summary>
    public FromNode Table { get; set; } = new();

    /// <summary>
    /// Gets or sets the ON condition.
    /// </summary>
    public ExpressionNode? OnCondition { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitJoin(this);
}

/// <summary>
/// Represents a WHERE clause.
/// </summary>
public class WhereNode : SqlNode
{
    /// <summary>
    /// Gets or sets the condition expression.
    /// </summary>
    public ExpressionNode Condition { get; set; } = new BinaryExpressionNode();

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitWhere(this);
}

/// <summary>
/// Represents a generic expression.
/// </summary>
public abstract class ExpressionNode : SqlNode
{
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
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitBinaryExpression(this);
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
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitLiteral(this);
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
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitColumnReference(this);
}

/// <summary>
/// Represents an IN expression.
/// </summary>
public class InExpressionNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the expression to test.
    /// </summary>
    public ExpressionNode? Expression { get; set; }

    /// <summary>
    /// Gets or sets the list of values.
    /// </summary>
    public List<ExpressionNode> Values { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this is a NOT IN expression.
    /// </summary>
    public bool IsNot { get; set; }

    /// <summary>
    /// Gets or sets a subquery for IN (SELECT ...).
    /// </summary>
    public SelectNode? Subquery { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitInExpression(this);
}

/// <summary>
/// Represents an ORDER BY clause.
/// </summary>
public class OrderByNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of order by items.
    /// </summary>
    public List<OrderByItem> Items { get; set; } = new();

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitOrderBy(this);
}

/// <summary>
/// Represents an item in the ORDER BY clause.
/// </summary>
public class OrderByItem
{
    /// <summary>
    /// Gets or sets the column reference.
    /// </summary>
    public ColumnReferenceNode Column { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this is ascending (default true).
    /// </summary>
    public bool IsAscending { get; set; } = true;
}

/// <summary>
/// Represents a GROUP BY clause.
/// </summary>
public class GroupByNode : SqlNode
{
    /// <summary>
    /// Gets or sets the list of grouping columns.
    /// </summary>
    public List<ColumnReferenceNode> Columns { get; set; } = new();

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitGroupBy(this);
}

/// <summary>
/// Represents a HAVING clause.
/// </summary>
public class HavingNode : SqlNode
{
    /// <summary>
    /// Gets or sets the condition expression.
    /// </summary>
    public ExpressionNode Condition { get; set; } = new BinaryExpressionNode();

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitHaving(this);
}

/// <summary>
/// Represents a function call.
/// </summary>
public class FunctionCallNode : ExpressionNode
{
    /// <summary>
    /// Gets or sets the function name.
    /// </summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the function arguments.
    /// </summary>
    public List<ExpressionNode> Arguments { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this is a DISTINCT aggregate.
    /// </summary>
    public bool IsDistinct { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitFunctionCall(this);
}

/// <summary>
/// Represents an INSERT statement.
/// </summary>
public class InsertNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of columns.
    /// </summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of values.
    /// </summary>
    public List<ExpressionNode> Values { get; set; } = new();

    /// <summary>
    /// Gets or sets a SELECT statement for INSERT INTO ... SELECT.
    /// </summary>
    public SelectNode? SelectStatement { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitInsert(this);
}

/// <summary>
/// Represents an UPDATE statement.
/// </summary>
public class UpdateNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SET assignments.
    /// </summary>
    public Dictionary<string, ExpressionNode> Assignments { get; set; } = new();

    /// <summary>
    /// Gets or sets the WHERE clause.
    /// </summary>
    public WhereNode? Where { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitUpdate(this);
}

/// <summary>
/// Represents a DELETE statement.
/// </summary>
public class DeleteNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the WHERE clause.
    /// </summary>
    public WhereNode? Where { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitDelete(this);
}

/// <summary>
/// Represents a CREATE TABLE statement.
/// </summary>
public class CreateTableNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column definitions.
    /// </summary>
    public List<ColumnDefinition> Columns { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to use IF NOT EXISTS.
    /// </summary>
    public bool IfNotExists { get; set; }

    /// <inheritdoc/>
    public override object? Accept(ISqlVisitor visitor) => visitor.VisitCreateTable(this);
}

/// <summary>
/// Represents a column definition in CREATE TABLE.
/// </summary>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data type.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets whether this is auto-increment.
    /// </summary>
    public bool IsAutoIncrement { get; set; }

    /// <summary>
    /// Gets or sets whether this is NOT NULL.
    /// </summary>
    public bool IsNotNull { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public object? DefaultValue { get; set; }
}
