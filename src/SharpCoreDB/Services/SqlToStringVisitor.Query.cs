// <copyright file="SqlToStringVisitor.Query.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SQL to string visitor for debugging/logging - Query partial class.
/// Handles query-related visitors: SELECT, FROM, JOIN, WHERE, GROUP BY, ORDER BY, HAVING.
/// </summary>
public sealed partial class SqlToStringVisitor
{
    /// <inheritdoc/>
    protected override string VisitSelectCore(SelectNode node)
    {
        List<string> parts = ["SELECT"]; // ✅ C# 14: Collection expression

        if (node.IsDistinct)
            parts.Add("DISTINCT");

        var columns = node.Columns.Select(c => c.Accept(this));
        parts.Add(string.Join(", ", columns));

        if (node.From is not null) // ✅ C# 14: is not null pattern
        {
            parts.Add("FROM");
            parts.Add(node.From.Accept(this));
        }

        if (node.Where is not null) // ✅ C# 14: is not null pattern
            parts.Add(node.Where.Accept(this));

        if (node.GroupBy is not null) // ✅ C# 14: is not null pattern
            parts.Add(node.GroupBy.Accept(this));

        if (node.Having is not null) // ✅ C# 14: is not null pattern
            parts.Add(node.Having.Accept(this));

        if (node.OrderBy is not null) // ✅ C# 14: is not null pattern
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
            return node.TableAlias is not null ? $"{node.TableAlias}.*" : "*"; // ✅ C# 14: is not null

        var col = node.TableAlias is not null // ✅ C# 14: is not null pattern
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
        var from = node.Subquery is not null // ✅ C# 14: is not null pattern
            ? $"({node.Subquery.Accept(this)})"
            : _dialect.QuoteIdentifier(node.TableName);

        if (!string.IsNullOrEmpty(node.Alias))
            from += $" AS {_dialect.QuoteIdentifier(node.Alias)}";

        var joins = node.Joins.Select(j => j.Accept(this));
        return string.Join(" ", [from, .. joins]); // ✅ C# 14: Collection expression + spread operator
    }

    /// <inheritdoc/>
    protected override string VisitJoinCore(JoinNode node)
    {
        var joinType = node.Type switch // ✅ C# 14: Switch expression
        {
            JoinNode.JoinType.Inner => "INNER JOIN",
            JoinNode.JoinType.Left => "LEFT OUTER JOIN",
            JoinNode.JoinType.Right => "RIGHT OUTER JOIN",
            JoinNode.JoinType.Full => "FULL OUTER JOIN",
            JoinNode.JoinType.Cross => "CROSS JOIN",
            _ => "JOIN"
        };

        var parts = new[] { joinType, node.Table.Accept(this) };

        if (node.OnCondition is not null) // ✅ C# 14: is not null pattern
            parts = [.. parts, $"ON {node.OnCondition.Accept(this)}"]; // ✅ C# 14: Collection expression + spread

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitWhereCore(WhereNode node) =>
        $"WHERE {node.Condition.Accept(this)}";

    /// <inheritdoc/>
    protected override string VisitBinaryExpressionCore(BinaryExpressionNode node)
    {
        var left = node.Left?.Accept(this) ?? "NULL"; // ✅ C# 14: Null-coalescing
        var right = node.Right?.Accept(this) ?? "NULL";
        return $"({left} {node.Operator} {right})";
    }

    /// <inheritdoc/>
    protected override string VisitLiteralCore(LiteralNode node) =>
        node.Value switch // ✅ C# 14: Switch expression with pattern matching
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "1" : "0",
            _ => node.Value.ToString() ?? "NULL"
        };

    /// <inheritdoc/>
    protected override string VisitColumnReferenceCore(ColumnReferenceNode node) =>
        node.TableAlias is not null // ✅ C# 14: is not null pattern
            ? $"{_dialect.QuoteIdentifier(node.TableAlias)}.{_dialect.QuoteIdentifier(node.ColumnName)}"
            : _dialect.QuoteIdentifier(node.ColumnName);

    /// <inheritdoc/>
    protected override string VisitInExpressionCore(InExpressionNode node)
    {
        var expr = node.Expression?.Accept(this) ?? "NULL";
        var op = node.IsNot ? "NOT IN" : "IN";

        if (node.Subquery is not null) // ✅ C# 14: is not null pattern
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
}
