// <copyright file="SqlVisitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Base visitor with error recovery capabilities.
/// Implements try/catch handling for each visit method to prevent crashes.
/// </summary>
/// <typeparam name="TResult">The return type of visit operations.</typeparam>
/// <param name="throwOnError">Whether to throw exceptions on errors (default: false).</param>
public abstract class SqlVisitorBase<TResult>(bool throwOnError = false) : ISqlVisitor<TResult>
{
    private readonly List<string> _errors = [];

    /// <summary>
    /// Gets the list of errors encountered during visitation.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets whether any errors were encountered.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Clears all recorded errors.
    /// </summary>
    public void ClearErrors() => _errors.Clear();

    /// <summary>
    /// Records an error message.
    /// </summary>
    protected void RecordError(string message, SqlNode? node = null)
    {
        var errorMsg = node != null
            ? $"[Position {node.Position}] {message}"
            : message;
        _errors.Add(errorMsg);
    }

    /// <summary>
    /// Safely executes a visit operation with error recovery.
    /// </summary>
    protected TResult SafeVisit(Func<TResult> visitFunc, string context, SqlNode? node = null)
    {
        try
        {
            return visitFunc();
        }
        catch (Exception ex)
        {
            RecordError($"{context}: {ex.Message}", node);
            if (throwOnError)
                throw;
            return default!;
        }
    }

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
}

/// <summary>
/// SQL to string visitor for debugging/logging.
/// Type-safe visitor that always returns strings.
/// </summary>
/// <param name="dialect">The SQL dialect to use.</param>
public sealed class SqlToStringVisitor(ISqlDialect? dialect = null) : SqlVisitorBase<string>(throwOnError: false)
{
    private readonly ISqlDialect _dialect = dialect ?? SqlDialectFactory.Default;

    /// <inheritdoc/>
    protected override string VisitSelectCore(SelectNode node)
    {
        List<string> parts = ["SELECT"];

        if (node.IsDistinct)
            parts.Add("DISTINCT");

        var columns = node.Columns.Select(c => c.Accept(this));
        parts.Add(string.Join(", ", columns));

        if (node.From != null)
        {
            parts.Add("FROM");
            parts.Add(node.From.Accept(this));
        }

        if (node.Where != null)
            parts.Add(node.Where.Accept(this));

        if (node.GroupBy != null)
            parts.Add(node.GroupBy.Accept(this));

        if (node.Having != null)
            parts.Add(node.Having.Accept(this));

        if (node.OrderBy != null)
            parts.Add(node.OrderBy.Accept(this));

        var limitClause = _dialect.FormatLimitClause(node.Limit, node.Offset);
        if (!string.IsNullOrEmpty(limitClause))
            parts.Add(limitClause);

        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <inheritdoc/>
    protected override string VisitColumnCore(ColumnNode node)
    {
        if (node.IsWildcard)
            return node.TableAlias != null ? $"{node.TableAlias}.*" : "*";

        var col = node.TableAlias != null
            ? $"{_dialect.QuoteIdentifier(node.TableAlias)}.{_dialect.QuoteIdentifier(node.Name)}"
            : _dialect.QuoteIdentifier(node.Name);

        if (!string.IsNullOrEmpty(node.AggregateFunction))
            col = $"{node.AggregateFunction}({col})";

        if (!string.IsNullOrEmpty(node.Alias))
            col += $" AS {_dialect.QuoteIdentifier(node.Alias)}";

        return col;
    }

    /// <inheritdoc/>
    protected override string VisitFromCore(FromNode node)
    {
        var from = node.Subquery != null
            ? $"({node.Subquery.Accept(this)})"
            : _dialect.QuoteIdentifier(node.TableName);

        if (!string.IsNullOrEmpty(node.Alias))
            from += $" AS {_dialect.QuoteIdentifier(node.Alias)}";

        var joins = node.Joins.Select(j => j.Accept(this));
        return string.Join(" ", [from, .. joins]);
    }

    /// <inheritdoc/>
    protected override string VisitJoinCore(JoinNode node)
    {
        var joinType = node.Type switch
        {
            JoinNode.JoinType.Inner => "INNER JOIN",
            JoinNode.JoinType.Left => "LEFT OUTER JOIN",
            JoinNode.JoinType.Right => "RIGHT OUTER JOIN",
            JoinNode.JoinType.Full => "FULL OUTER JOIN",
            JoinNode.JoinType.Cross => "CROSS JOIN",
            _ => "JOIN"
        };

        var parts = new[] { joinType, node.Table.Accept(this) };

        if (node.OnCondition != null)
            parts = [.. parts, $"ON {node.OnCondition.Accept(this)}"];

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitWhereCore(WhereNode node) =>
        $"WHERE {node.Condition.Accept(this)}";

    /// <inheritdoc/>
    protected override string VisitBinaryExpressionCore(BinaryExpressionNode node)
    {
        var left = node.Left?.Accept(this) ?? "NULL";
        var right = node.Right?.Accept(this) ?? "NULL";
        return $"({left} {node.Operator} {right})";
    }

    /// <inheritdoc/>
    protected override string VisitLiteralCore(LiteralNode node) =>
        node.Value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "1" : "0",
            _ => node.Value.ToString() ?? "NULL"
        };

    /// <inheritdoc/>
    protected override string VisitColumnReferenceCore(ColumnReferenceNode node) =>
        node.TableAlias != null
            ? $"{_dialect.QuoteIdentifier(node.TableAlias)}.{_dialect.QuoteIdentifier(node.ColumnName)}"
            : _dialect.QuoteIdentifier(node.ColumnName);

    /// <inheritdoc/>
    protected override string VisitInExpressionCore(InExpressionNode node)
    {
        var expr = node.Expression?.Accept(this) ?? "NULL";
        var op = node.IsNot ? "NOT IN" : "IN";

        if (node.Subquery != null)
            return $"{expr} {op} ({node.Subquery.Accept(this)})";

        var values = node.Values.Select(v => v.Accept(this));
        return $"{expr} {op} ({string.Join(", ", values)})";
    }

    /// <inheritdoc/>
    protected override string VisitOrderByCore(OrderByNode node)
    {
        var items = node.Items.Select(i =>
        {
            var col = i.Column.Accept(this);
            return i.IsAscending ? col : $"{col} DESC";
        });
        return $"ORDER BY {string.Join(", ", items)}";
    }

    /// <inheritdoc/>
    protected override string VisitGroupByCore(GroupByNode node)
    {
        var columns = node.Columns.Select(c => c.Accept(this));
        return $"GROUP BY {string.Join(", ", columns)}";
    }

    /// <inheritdoc/>
    protected override string VisitHavingCore(HavingNode node) =>
        $"HAVING {node.Condition.Accept(this)}";

    /// <inheritdoc/>
    protected override string VisitFunctionCallCore(FunctionCallNode node)
    {
        var funcName = _dialect.TranslateFunction(node.FunctionName);
        var distinct = node.IsDistinct ? "DISTINCT " : "";
        var args = node.Arguments.Select(a => a.Accept(this));
        return $"{funcName}({distinct}{string.Join(", ", args)})";
    }

    /// <inheritdoc/>
    protected override string VisitInsertCore(InsertNode node)
    {
        List<string> parts = ["INSERT INTO", _dialect.QuoteIdentifier(node.TableName)];

        if (node.Columns.Any())
        {
            var cols = string.Join(", ", node.Columns.Select(_dialect.QuoteIdentifier));
            parts.Add($"({cols})");
        }

        if (node.SelectStatement != null)
        {
            parts.Add(node.SelectStatement.Accept(this));
        }
        else
        {
            var values = node.Values.Select(v => v.Accept(this));
            parts.Add($"VALUES ({string.Join(", ", values)})");
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitUpdateCore(UpdateNode node)
    {
        List<string> parts = ["UPDATE", _dialect.QuoteIdentifier(node.TableName), "SET"];

        var sets = node.Assignments.Select(kv =>
            $"{_dialect.QuoteIdentifier(kv.Key)} = {kv.Value.Accept(this)}");
        parts.Add(string.Join(", ", sets));

        if (node.Where != null)
            parts.Add(node.Where.Accept(this));

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitDeleteCore(DeleteNode node)
    {
        List<string> parts = ["DELETE FROM", _dialect.QuoteIdentifier(node.TableName)];

        if (node.Where != null)
            parts.Add(node.Where.Accept(this));

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitCreateTableCore(CreateTableNode node)
    {
        List<string> parts = ["CREATE TABLE"];

        if (node.IfNotExists)
            parts.Add("IF NOT EXISTS");

        parts.Add(_dialect.QuoteIdentifier(node.TableName));

        var columns = node.Columns.Select(c =>
        {
            var def = $"{_dialect.QuoteIdentifier(c.Name)} {c.DataType}";
            if (c.IsPrimaryKey) def += " PRIMARY KEY";
            if (c.IsAutoIncrement) def += " AUTO";
            if (c.IsNotNull) def += " NOT NULL";
            if (c.DefaultValue != null) def += $" DEFAULT {c.DefaultValue}";
            return def;
        });

        parts.Add($"({string.Join(", ", columns)})");

        return string.Join(" ", parts);
    }
}
