// <copyright file="SqlAst.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// SqlAst - Core infrastructure and visitor pattern.
/// Contains base classes, interfaces, and visitor definitions.
/// Part of the SqlAst partial class infrastructure.
/// Modern C# 14 with covariant returns and pattern matching.
/// See also: SqlAst.Nodes.cs, SqlAst.DML.cs
/// </summary>
public static partial class SqlAst
{
    // This partial contains only the namespace placeholder.
    // All types are defined at namespace level.
}

/// <summary>
/// Generic visitor interface for SQL AST nodes with type-safe return values.
/// ✅ C# 14: Covariant return type (out TResult).
/// </summary>
/// <typeparam name="TResult">The return type of visit operations.</typeparam>
public interface ISqlVisitor<out TResult>
{
    /// <summary>Visits a SELECT node.</summary>
    TResult VisitSelect(SelectNode node);

    /// <summary>Visits a column node.</summary>
    TResult VisitColumn(ColumnNode node);

    /// <summary>Visits a FROM node.</summary>
    TResult VisitFrom(FromNode node);

    /// <summary>Visits a JOIN node.</summary>
    TResult VisitJoin(JoinNode node);

    /// <summary>Visits a WHERE node.</summary>
    TResult VisitWhere(WhereNode node);

    /// <summary>Visits a binary expression node.</summary>
    TResult VisitBinaryExpression(BinaryExpressionNode node);

    /// <summary>Visits a literal node.</summary>
    TResult VisitLiteral(LiteralNode node);

    /// <summary>Visits a column reference node.</summary>
    TResult VisitColumnReference(ColumnReferenceNode node);

    /// <summary>Visits an IN expression node.</summary>
    TResult VisitInExpression(InExpressionNode node);

    /// <summary>Visits an ORDER BY node.</summary>
    TResult VisitOrderBy(OrderByNode node);

    /// <summary>Visits a GROUP BY node.</summary>
    TResult VisitGroupBy(GroupByNode node);

    /// <summary>Visits a HAVING node.</summary>
    TResult VisitHaving(HavingNode node);

    /// <summary>Visits a function call node.</summary>
    TResult VisitFunctionCall(FunctionCallNode node);

    /// <summary>Visits a GRAPH_TRAVERSE node.</summary>
    TResult VisitGraphTraverse(GraphTraverseNode node);

    /// <summary>Visits an INSERT node.</summary>
    TResult VisitInsert(InsertNode node);

    /// <summary>Visits an UPDATE node.</summary>
    TResult VisitUpdate(UpdateNode node);

    /// <summary>Visits a DELETE node.</summary>
    TResult VisitDelete(DeleteNode node);

    /// <summary>Visits a CREATE TABLE node.</summary>
    TResult VisitCreateTable(CreateTableNode node);

    /// <summary>Visits an ALTER TABLE node.</summary>
    TResult VisitAlterTable(AlterTableNode node);
}

/// <summary>
/// Non-generic visitor interface for backward compatibility.
/// </summary>
public interface ISqlVisitor : ISqlVisitor<object?>
{
}

/// <summary>
/// Abstract base class for SQL AST nodes.
/// ✅ C# 14: Required members could be used in derived classes.
/// </summary>
public abstract class SqlNode
{
    /// <summary>
    /// Gets or sets the source position for error reporting.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Accepts a visitor for the visitor pattern.
    /// ✅ C# 14: Generic visitor pattern with covariant returns.
    /// </summary>
    /// <typeparam name="TResult">The return type of the visitor.</typeparam>
    /// <param name="visitor">The visitor instance.</param>
    /// <returns>Result of the visit.</returns>
    public abstract TResult Accept<TResult>(ISqlVisitor<TResult> visitor);
}

/// <summary>
/// Represents a generic expression.
/// Base class for all expression nodes in the AST.
/// </summary>
public abstract class ExpressionNode : SqlNode
{
}
