// <copyright file="SqlVisitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
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
/// <remarks>
/// Initializes a new instance of the <see cref="SqlVisitorBase"/> class.
/// </remarks>
/// <param name="throwOnError">Whether to throw exceptions on errors (default: false).</param>
public abstract class SqlVisitorBase(bool throwOnError = false) : ISqlVisitor
{
    private readonly List<string> _errors = [];
    private readonly bool _throwOnError = throwOnError;

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
    protected object? SafeVisit(Func<object?> visitFunc, string context, SqlNode? node = null)
    {
        try
        {
            return visitFunc();
        }
        catch (Exception ex)
        {
            RecordError($"{context}: {ex.Message}", node);
            if (_throwOnError)
                throw;
            return null;
        }
    }

    /// <inheritdoc/>
    public virtual object? VisitSelect(SelectNode node)
    {
        return SafeVisit(() => VisitSelectCore(node), "SELECT", node);
    }

    /// <summary>
    /// Core implementation of SELECT visit (without error recovery).
    /// </summary>
    protected abstract object? VisitSelectCore(SelectNode node);

    /// <inheritdoc/>
    public virtual object? VisitColumn(ColumnNode node)
    {
        return SafeVisit(() => VisitColumnCore(node), "COLUMN", node);
    }

    /// <summary>
    /// Core implementation of COLUMN visit.
    /// </summary>
    protected abstract object? VisitColumnCore(ColumnNode node);

    /// <inheritdoc/>
    public virtual object? VisitFrom(FromNode node)
    {
        return SafeVisit(() => VisitFromCore(node), "FROM", node);
    }

    /// <summary>
    /// Core implementation of FROM visit.
    /// </summary>
    protected abstract object? VisitFromCore(FromNode node);

    /// <inheritdoc/>
    public virtual object? VisitJoin(JoinNode node)
    {
        return SafeVisit(() => VisitJoinCore(node), "JOIN", node);
    }

    /// <summary>
    /// Core implementation of JOIN visit.
    /// </summary>
    protected abstract object? VisitJoinCore(JoinNode node);

    /// <inheritdoc/>
    public virtual object? VisitWhere(WhereNode node)
    {
        return SafeVisit(() => VisitWhereCore(node), "WHERE", node);
    }

    /// <summary>
    /// Core implementation of WHERE visit.
    /// </summary>
    protected abstract object? VisitWhereCore(WhereNode node);

    /// <inheritdoc/>
    public virtual object? VisitBinaryExpression(BinaryExpressionNode node)
    {
        return SafeVisit(() => VisitBinaryExpressionCore(node), "BINARY EXPRESSION", node);
    }

    /// <summary>
    /// Core implementation of BINARY EXPRESSION visit.
    /// </summary>
    protected abstract object? VisitBinaryExpressionCore(BinaryExpressionNode node);

    /// <inheritdoc/>
    public virtual object? VisitLiteral(LiteralNode node)
    {
        return SafeVisit(() => VisitLiteralCore(node), "LITERAL", node);
    }

    /// <summary>
    /// Core implementation of LITERAL visit.
    /// </summary>
    protected abstract object? VisitLiteralCore(LiteralNode node);

    /// <inheritdoc/>
    public virtual object? VisitColumnReference(ColumnReferenceNode node)
    {
        return SafeVisit(() => VisitColumnReferenceCore(node), "COLUMN REFERENCE", node);
    }

    /// <summary>
    /// Core implementation of COLUMN REFERENCE visit.
    /// </summary>
    protected abstract object? VisitColumnReferenceCore(ColumnReferenceNode node);

    /// <inheritdoc/>
    public virtual object? VisitInExpression(InExpressionNode node)
    {
        return SafeVisit(() => VisitInExpressionCore(node), "IN EXPRESSION", node);
    }

    /// <summary>
    /// Core implementation of IN EXPRESSION visit.
    /// </summary>
    protected abstract object? VisitInExpressionCore(InExpressionNode node);

    /// <inheritdoc/>
    public virtual object? VisitOrderBy(OrderByNode node)
    {
        return SafeVisit(() => VisitOrderByCore(node), "ORDER BY", node);
    }

    /// <summary>
    /// Core implementation of ORDER BY visit.
    /// </summary>
    protected abstract object? VisitOrderByCore(OrderByNode node);

    /// <inheritdoc/>
    public virtual object? VisitGroupBy(GroupByNode node)
    {
        return SafeVisit(() => VisitGroupByCore(node), "GROUP BY", node);
    }

    /// <summary>
    /// Core implementation of GROUP BY visit.
    /// </summary>
    protected abstract object? VisitGroupByCore(GroupByNode node);

    /// <inheritdoc/>
    public virtual object? VisitHaving(HavingNode node)
    {
        return SafeVisit(() => VisitHavingCore(node), "HAVING", node);
    }

    /// <summary>
    /// Core implementation of HAVING visit.
    /// </summary>
    protected abstract object? VisitHavingCore(HavingNode node);

    /// <inheritdoc/>
    public virtual object? VisitFunctionCall(FunctionCallNode node)
    {
        return SafeVisit(() => VisitFunctionCallCore(node), "FUNCTION CALL", node);
    }

    /// <summary>
    /// Core implementation of FUNCTION CALL visit.
    /// </summary>
    protected abstract object? VisitFunctionCallCore(FunctionCallNode node);

    /// <inheritdoc/>
    public virtual object? VisitInsert(InsertNode node)
    {
        return SafeVisit(() => VisitInsertCore(node), "INSERT", node);
    }

    /// <summary>
    /// Core implementation of INSERT visit.
    /// </summary>
    protected abstract object? VisitInsertCore(InsertNode node);

    /// <inheritdoc/>
    public virtual object? VisitUpdate(UpdateNode node)
    {
        return SafeVisit(() => VisitUpdateCore(node), "UPDATE", node);
    }

    /// <summary>
    /// Core implementation of UPDATE visit.
    /// </summary>
    protected abstract object? VisitUpdateCore(UpdateNode node);

    /// <inheritdoc/>
    public virtual object? VisitDelete(DeleteNode node)
    {
        return SafeVisit(() => VisitDeleteCore(node), "DELETE", node);
    }

    /// <summary>
    /// Core implementation of DELETE visit.
    /// </summary>
    protected abstract object? VisitDeleteCore(DeleteNode node);

    /// <inheritdoc/>
    public virtual object? VisitCreateTable(CreateTableNode node) => SafeVisit(() => VisitCreateTableCore(node), "CREATE TABLE", node);

    /// <summary>
    /// Core implementation of CREATE TABLE visit.
    /// </summary>
    protected abstract object? VisitCreateTableCore(CreateTableNode node);
}

/// <summary>
/// SQL to string visitor for debugging/logging.
/// </summary>
public class SqlToStringVisitor : SqlVisitorBase
{
    private readonly ISqlDialect _dialect;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlToStringVisitor"/> class.
    /// </summary>
    /// <param name="dialect">The SQL dialect to use.</param>
    public SqlToStringVisitor(ISqlDialect? dialect = null)
        : base(throwOnError: false)
    {
        _dialect = dialect ?? SqlDialectFactory.Default;
    }

    /// <inheritdoc/>
    protected override object? VisitSelectCore(SelectNode node)
    {
        var parts = new List<string>();
        parts.Add("SELECT");

        if (node.IsDistinct)
            parts.Add("DISTINCT");

        var columns = node.Columns.Select(c => c.Accept(this)?.ToString() ?? "*");
        parts.Add(string.Join(", ", columns));

        if (node.From != null)
        {
            parts.Add("FROM");
            parts.Add(node.From.Accept(this)?.ToString() ?? "");
        }

        if (node.Where != null)
            parts.Add(node.Where.Accept(this)?.ToString() ?? "");

        if (node.GroupBy != null)
            parts.Add(node.GroupBy.Accept(this)?.ToString() ?? "");

        if (node.Having != null)
            parts.Add(node.Having.Accept(this)?.ToString() ?? "");

        if (node.OrderBy != null)
            parts.Add(node.OrderBy.Accept(this)?.ToString() ?? "");

        var limitClause = _dialect.FormatLimitClause(node.Limit, node.Offset);
        if (!string.IsNullOrEmpty(limitClause))
            parts.Add(limitClause);

        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <inheritdoc/>
    protected override object? VisitColumnCore(ColumnNode node)
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
    protected override object? VisitFromCore(FromNode node)
    {
        var from = node.Subquery != null
            ? $"({node.Subquery.Accept(this)})"
            : _dialect.QuoteIdentifier(node.TableName);

        if (!string.IsNullOrEmpty(node.Alias))
            from += $" AS {_dialect.QuoteIdentifier(node.Alias)}";

        var joins = node.Joins.Select(j => j.Accept(this)?.ToString() ?? "");
        return string.Join(" ", new[] { from }.Concat(joins));
    }

    /// <inheritdoc/>
    protected override object? VisitJoinCore(JoinNode node)
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

        var parts = new[] { joinType, node.Table.Accept(this)?.ToString() ?? "" };

        if (node.OnCondition != null)
            parts = parts.Append($"ON {node.OnCondition.Accept(this)}").ToArray();

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override object? VisitWhereCore(WhereNode node)
    {
        return $"WHERE {node.Condition.Accept(this)}";
    }

    /// <inheritdoc/>
    protected override object? VisitBinaryExpressionCore(BinaryExpressionNode node)
    {
        var left = node.Left?.Accept(this)?.ToString() ?? "NULL";
        var right = node.Right?.Accept(this)?.ToString() ?? "NULL";
        return $"({left} {node.Operator} {right})";
    }

    /// <inheritdoc/>
    protected override object? VisitLiteralCore(LiteralNode node)
    {
        if (node.Value == null)
            return "NULL";

        if (node.Value is string s)
            return $"'{s.Replace("'", "''")}'";

        if (node.Value is bool b)
            return b ? "1" : "0";

        return node.Value.ToString();
    }

    /// <inheritdoc/>
    protected override object? VisitColumnReferenceCore(ColumnReferenceNode node)
    {
        return node.TableAlias != null
            ? $"{_dialect.QuoteIdentifier(node.TableAlias)}.{_dialect.QuoteIdentifier(node.ColumnName)}"
            : _dialect.QuoteIdentifier(node.ColumnName);
    }

    /// <inheritdoc/>
    protected override object? VisitInExpressionCore(InExpressionNode node)
    {
        var expr = node.Expression?.Accept(this)?.ToString() ?? "NULL";
        var op = node.IsNot ? "NOT IN" : "IN";

        if (node.Subquery != null)
            return $"{expr} {op} ({node.Subquery.Accept(this)})";

        var values = node.Values.Select(v => v.Accept(this)?.ToString() ?? "NULL");
        return $"{expr} {op} ({string.Join(", ", values)})";
    }

    /// <inheritdoc/>
    protected override object? VisitOrderByCore(OrderByNode node)
    {
        var items = node.Items.Select(i =>
        {
            var col = i.Column.Accept(this)?.ToString() ?? "";
            return i.IsAscending ? col : $"{col} DESC";
        });
        return $"ORDER BY {string.Join(", ", items)}";
    }

    /// <inheritdoc/>
    protected override object? VisitGroupByCore(GroupByNode node)
    {
        var columns = node.Columns.Select(c => c.Accept(this)?.ToString() ?? "");
        return $"GROUP BY {string.Join(", ", columns)}";
    }

    /// <inheritdoc/>
    protected override object? VisitHavingCore(HavingNode node)
    {
        return $"HAVING {node.Condition.Accept(this)}";
    }

    /// <inheritdoc/>
    protected override object? VisitFunctionCallCore(FunctionCallNode node)
    {
        var funcName = _dialect.TranslateFunction(node.FunctionName);
        var distinct = node.IsDistinct ? "DISTINCT " : "";
        var args = node.Arguments.Select(a => a.Accept(this)?.ToString() ?? "");
        return $"{funcName}({distinct}{string.Join(", ", args)})";
    }

    /// <inheritdoc/>
    protected override object? VisitInsertCore(InsertNode node)
    {
        var parts = new List<string> { "INSERT INTO", _dialect.QuoteIdentifier(node.TableName) };

        if (node.Columns.Any())
        {
            var cols = string.Join(", ", node.Columns.Select(_dialect.QuoteIdentifier));
            parts.Add($"({cols})");
        }

        if (node.SelectStatement != null)
        {
            parts.Add(node.SelectStatement.Accept(this)?.ToString() ?? "");
        }
        else
        {
            var values = node.Values.Select(v => v.Accept(this)?.ToString() ?? "NULL");
            parts.Add($"VALUES ({string.Join(", ", values)})");
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override object? VisitUpdateCore(UpdateNode node)
    {
        var parts = new List<string> { "UPDATE", _dialect.QuoteIdentifier(node.TableName), "SET" };

        var sets = node.Assignments.Select(kv =>
            $"{_dialect.QuoteIdentifier(kv.Key)} = {kv.Value.Accept(this)}");
        parts.Add(string.Join(", ", sets));

        if (node.Where != null)
            parts.Add(node.Where.Accept(this)?.ToString() ?? "");

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override object? VisitDeleteCore(DeleteNode node)
    {
        var parts = new List<string> { "DELETE FROM", _dialect.QuoteIdentifier(node.TableName) };

        if (node.Where != null)
            parts.Add(node.Where.Accept(this)?.ToString() ?? "");

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override object? VisitCreateTableCore(CreateTableNode node)
    {
        var parts = new List<string> { "CREATE TABLE" };

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
