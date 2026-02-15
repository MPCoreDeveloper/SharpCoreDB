// <copyright file="SqlVisitorBase.Visitors.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// Base visitor with error recovery capabilities - Visitors partial class.
/// Contains all visitor methods with SafeVisit wrappers and abstract core implementations.
/// </summary>
public abstract partial class SqlVisitorBase<TResult>
{
    /// <inheritdoc/>
    public virtual TResult VisitSelect(SelectNode node) =>
        SafeVisit(() => VisitSelectCore(node), "SELECT", node);

    /// <summary>
    /// Core implementation of SELECT visit (without error recovery).
    /// </summary>
    protected abstract TResult VisitSelectCore(SelectNode node);

    /// <inheritdoc/>
    public virtual TResult VisitColumn(ColumnNode node) =>
        SafeVisit(() => VisitColumnCore(node), "COLUMN", node);

    /// <summary>
    /// Core implementation of COLUMN visit.
    /// </summary>
    protected abstract TResult VisitColumnCore(ColumnNode node);

    /// <inheritdoc/>
    public virtual TResult VisitFrom(FromNode node) =>
        SafeVisit(() => VisitFromCore(node), "FROM", node);

    /// <summary>
    /// Core implementation of FROM visit.
    /// </summary>
    protected abstract TResult VisitFromCore(FromNode node);

    /// <inheritdoc/>
    public virtual TResult VisitJoin(JoinNode node) =>
        SafeVisit(() => VisitJoinCore(node), "JOIN", node);

    /// <summary>
    /// Core implementation of JOIN visit.
    /// </summary>
    protected abstract TResult VisitJoinCore(JoinNode node);

    /// <inheritdoc/>
    public virtual TResult VisitWhere(WhereNode node) =>
        SafeVisit(() => VisitWhereCore(node), "WHERE", node);

    /// <summary>
    /// Core implementation of WHERE visit.
    /// </summary>
    protected abstract TResult VisitWhereCore(WhereNode node);

    /// <inheritdoc/>
    public virtual TResult VisitBinaryExpression(BinaryExpressionNode node) =>
        SafeVisit(() => VisitBinaryExpressionCore(node), "BINARY EXPRESSION", node);

    /// <summary>
    /// Core implementation of BINARY EXPRESSION visit.
    /// </summary>
    protected abstract TResult VisitBinaryExpressionCore(BinaryExpressionNode node);

    /// <inheritdoc/>
    public virtual TResult VisitLiteral(LiteralNode node) =>
        SafeVisit(() => VisitLiteralCore(node), "LITERAL", node);

    /// <summary>
    /// Core implementation of LITERAL visit.
    /// </summary>
    protected abstract TResult VisitLiteralCore(LiteralNode node);

    /// <inheritdoc/>
    public virtual TResult VisitColumnReference(ColumnReferenceNode node) =>
        SafeVisit(() => VisitColumnReferenceCore(node), "COLUMN REFERENCE", node);

    /// <summary>
    /// Core implementation of COLUMN REFERENCE visit.
    /// </summary>
    protected abstract TResult VisitColumnReferenceCore(ColumnReferenceNode node);

    /// <inheritdoc/>
    public virtual TResult VisitInExpression(InExpressionNode node) =>
        SafeVisit(() => VisitInExpressionCore(node), "IN EXPRESSION", node);

    /// <summary>
    /// Core implementation of IN EXPRESSION visit.
    /// </summary>
    protected abstract TResult VisitInExpressionCore(InExpressionNode node);

    /// <inheritdoc/>
    public virtual TResult VisitOrderBy(OrderByNode node) =>
        SafeVisit(() => VisitOrderByCore(node), "ORDER BY", node);

    /// <summary>
    /// Core implementation of ORDER BY visit.
    /// </summary>
    protected abstract TResult VisitOrderByCore(OrderByNode node);

    /// <inheritdoc/>
    public virtual TResult VisitGroupBy(GroupByNode node) =>
        SafeVisit(() => VisitGroupByCore(node), "GROUP BY", node);

    /// <summary>
    /// Core implementation of GROUP BY visit.
    /// </summary>
    protected abstract TResult VisitGroupByCore(GroupByNode node);

    /// <inheritdoc/>
    public virtual TResult VisitHaving(HavingNode node) =>
        SafeVisit(() => VisitHavingCore(node), "HAVING", node);

    /// <summary>
    /// Core implementation of HAVING visit.
    /// </summary>
    protected abstract TResult VisitHavingCore(HavingNode node);

    /// <inheritdoc/>
    public virtual TResult VisitFunctionCall(FunctionCallNode node) =>
        SafeVisit(() => VisitFunctionCallCore(node), "FUNCTION CALL", node);

    /// <summary>
    /// Core implementation of FUNCTION CALL visit.
    /// </summary>
    protected abstract TResult VisitFunctionCallCore(FunctionCallNode node);

    /// <inheritdoc/>
    public virtual TResult VisitGraphTraverse(GraphTraverseNode node) =>
        SafeVisit(() => VisitGraphTraverseCore(node), "GRAPH_TRAVERSE", node);

    /// <summary>
    /// Core implementation of GRAPH_TRAVERSE visit.
    /// </summary>
    protected abstract TResult VisitGraphTraverseCore(GraphTraverseNode node);

    /// <inheritdoc/>
    public virtual TResult VisitInsert(InsertNode node) =>
        SafeVisit(() => VisitInsertCore(node), "INSERT", node);

    /// <summary>
    /// Core implementation of INSERT visit.
    /// </summary>
    protected abstract TResult VisitInsertCore(InsertNode node);

    /// <inheritdoc/>
    public virtual TResult VisitUpdate(UpdateNode node) =>
        SafeVisit(() => VisitUpdateCore(node), "UPDATE", node);

    /// <summary>
    /// Core implementation of UPDATE visit.
    /// </summary>
    protected abstract TResult VisitUpdateCore(UpdateNode node);

    /// <inheritdoc/>
    public virtual TResult VisitDelete(DeleteNode node) =>
        SafeVisit(() => VisitDeleteCore(node), "DELETE", node);

    /// <summary>
    /// Core implementation of DELETE visit.
    /// </summary>
    protected abstract TResult VisitDeleteCore(DeleteNode node);

    /// <inheritdoc/>
    public virtual TResult VisitCreateTable(CreateTableNode node) =>
        SafeVisit(() => VisitCreateTableCore(node), "CREATE TABLE", node);

    /// <summary>
    /// Core implementation of CREATE TABLE visit.
    /// </summary>
    protected abstract TResult VisitCreateTableCore(CreateTableNode node);

    /// <inheritdoc/>
    public virtual TResult VisitAlterTable(AlterTableNode node) =>
        SafeVisit(() => VisitAlterTableCore(node), "ALTER TABLE", node);

    /// <summary>
    /// Core implementation of ALTER TABLE visit.
    /// </summary>
    protected abstract TResult VisitAlterTableCore(AlterTableNode node);
}
