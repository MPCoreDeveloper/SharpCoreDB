// <copyright file="SqlParser.DML.InsertConflict.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System.Text.RegularExpressions;

/// <summary>
/// INSERT conflict policy helpers: INSERT OR … / ON CONFLICT DO NOTHING / ON CONFLICT DO UPDATE SET.
/// </summary>
public partial class SqlParser
{
    private bool ShouldSkipInsertForConflictPolicy(
        string tableName,
        ITable table,
        Dictionary<string, object> row,
        InsertConflictPolicy conflictPolicy,
        InsertOnConflictAction onConflictAction,
        List<string> conflictTargetColumns,
        Dictionary<string, string>? doUpdateAssignments = null,
        string? doUpdateWhere = null)
    {
        bool isDoNothing = onConflictAction is InsertOnConflictAction.DoNothing;
        bool isDoUpdate  = onConflictAction is InsertOnConflictAction.DoUpdate;

        if (conflictPolicy is InsertConflictPolicy.None && !isDoNothing && !isDoUpdate)
            return false;

        if (!TryDetectInsertConflict(table, row, conflictTargetColumns,
                out var conflictingPrimaryKey, out var conflictMessage))
            return false;

        if (isDoNothing)
            return true;

        if (isDoUpdate && conflictingPrimaryKey is not null && doUpdateAssignments is not null)
        {
            HandleDoUpdateConflict(tableName, table, conflictingPrimaryKey, row,
                doUpdateAssignments, doUpdateWhere);
            return true;
        }

        return conflictPolicy switch
        {
            InsertConflictPolicy.Ignore  => true,
            InsertConflictPolicy.Replace => conflictingPrimaryKey is not null
                ? !HandleReplaceConflict(tableName, conflictingPrimaryKey)
                : throw new InvalidOperationException(conflictMessage),
            InsertConflictPolicy.Fail    => throw new InvalidOperationException(conflictMessage),
            InsertConflictPolicy.Abort   => throw new InvalidOperationException(conflictMessage),
            _                            => false,
        };
    }

    private bool TryDetectInsertConflict(
        ITable table,
        Dictionary<string, object> row,
        List<string> conflictTargetColumns,
        out object? conflictingPrimaryKey,
        out string conflictMessage)
    {
        conflictingPrimaryKey = null;
        conflictMessage = string.Empty;

        bool restrictToTarget = conflictTargetColumns.Count > 0;

        if (table.PrimaryKeyIndex >= 0)
        {
            var pkColumn = table.Columns[table.PrimaryKeyIndex];
            bool pkTargeted = !restrictToTarget ||
                conflictTargetColumns.Contains(pkColumn, StringComparer.OrdinalIgnoreCase);

            if (pkTargeted &&
                row.TryGetValue(pkColumn, out var pkValue) &&
                pkValue is not null and not DBNull)
            {
                if (table.FindByPrimaryKey(pkValue) is not null)
                {
                    conflictingPrimaryKey = pkValue;
                    conflictMessage = $"Duplicate key value '{pkValue}' violates primary key constraint on '{pkColumn}'";
                    return true;
                }
            }
        }

        if (table is Table concreteTable &&
            concreteTable.TryGetConflictingUniquePrimaryKey(row, conflictTargetColumns,
                out conflictingPrimaryKey) &&
            conflictingPrimaryKey is not null)
        {
            conflictMessage = "Duplicate key value violates a UNIQUE constraint";
            return true;
        }

        return false;
    }

    private void RollbackInsertedRows(string tableName, List<object> insertedPrimaryKeys)
    {
        if (insertedPrimaryKeys.Count == 0)
            return;

        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException(
                $"Table {tableName} does not exist for statement rollback");

        for (int i = insertedPrimaryKeys.Count - 1; i >= 0; i--)
        {
            if (!table.DeleteByPrimaryKey(insertedPrimaryKeys[i]))
                throw new InvalidOperationException(
                    $"Failed to roll back inserted row with primary key '{insertedPrimaryKeys[i]}' after INSERT OR ABORT conflict");
        }
    }

    private bool HandleReplaceConflict(string tableName, object pkValue)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return false;

        return table.DeleteByPrimaryKey(pkValue);
    }

    /// <summary>
    /// Applies DO UPDATE SET assignments to the conflicting row.
    /// Supports excluded.col references (incoming values), bare column names (existing values), and literals.
    /// Optionally skips the update when doUpdateWhere evaluates to false.
    /// </summary>
    private void HandleDoUpdateConflict(
        string tableName,
        ITable table,
        object conflictingPrimaryKey,
        Dictionary<string, object> incomingRow,
        Dictionary<string, string> assignments,
        string? whereExpression)
    {
        var existingRow = table.FindByPrimaryKey(conflictingPrimaryKey);
        if (existingRow is null)
            return;

        if (!string.IsNullOrWhiteSpace(whereExpression) &&
            !EvaluateDoUpdateWhere(whereExpression, existingRow, incomingRow, table))
        {
            return;
        }

        var updates = new Dictionary<string, object>();
        foreach (var (colName, exprStr) in assignments)
        {
            var colIdx = table.Columns.IndexOf(colName);
            if (colIdx < 0)
                continue;

            var targetType = table.ColumnTypes[colIdx];
            var value = ResolveDoUpdateExpression(exprStr, incomingRow, existingRow, targetType);
            if (value is not null)
                updates[colName] = value;
        }

        if (updates.Count > 0 && table is Table concreteTable)
            concreteTable.UpdateByPrimaryKey(conflictingPrimaryKey, updates);
    }

    /// <summary>
    /// Resolves a DO UPDATE SET expression string to a concrete value.
    /// Priority: excluded.col → bare existing column → literal.
    /// </summary>
    private static object? ResolveDoUpdateExpression(
        string expr,
        Dictionary<string, object> incomingRow,
        Dictionary<string, object> existingRow,
        DataType targetType)
    {
        expr = expr.Trim();

        if (expr.StartsWith("excluded.", StringComparison.OrdinalIgnoreCase))
        {
            var col = expr["excluded.".Length..].Trim();
            return incomingRow.TryGetValue(col, out var v) ? v : null;
        }

        if (existingRow.TryGetValue(expr, out var existingVal))
            return existingVal;

        return SqlParser.ParseValue(expr, targetType);
    }

    /// <summary>
    /// Evaluates the DO UPDATE WHERE expression against the existing and incoming rows.
    /// Supports IS NULL / IS NOT NULL, col op excluded.col, and col op literal.
    /// </summary>
    private static bool EvaluateDoUpdateWhere(
        string whereExpr,
        Dictionary<string, object> existingRow,
        Dictionary<string, object> incomingRow,
        ITable table)
    {
        whereExpr = whereExpr.Trim();

        var isNotNullMatch = Regex.Match(whereExpr, @"^(\w+)\s+IS\s+NOT\s+NULL$", RegexOptions.IgnoreCase);
        if (isNotNullMatch.Success)
        {
            var col = isNotNullMatch.Groups[1].Value;
            return existingRow.TryGetValue(col, out var v) && v is not null and not DBNull;
        }

        var isNullMatch = Regex.Match(whereExpr, @"^(\w+)\s+IS\s+NULL$", RegexOptions.IgnoreCase);
        if (isNullMatch.Success)
        {
            var col = isNullMatch.Groups[1].Value;
            return !existingRow.TryGetValue(col, out var v) || v is null or DBNull;
        }

        var excludedOpMatch = Regex.Match(
            whereExpr, @"^(\w+)\s*([<>=!]+)\s*excluded\.(\w+)$", RegexOptions.IgnoreCase);
        if (excludedOpMatch.Success)
        {
            var leftCol  = excludedOpMatch.Groups[1].Value;
            var op       = excludedOpMatch.Groups[2].Value;
            var rightCol = excludedOpMatch.Groups[3].Value;
            existingRow.TryGetValue(leftCol,  out var leftVal);
            incomingRow.TryGetValue(rightCol, out var rightVal);
            return SqlParser.AreValuesEqual(leftVal, rightVal)
                ? op is "=" or "==" or ">=" or "<="
                : ConflictCompareValues(leftVal, rightVal, op);
        }

        var literalOpMatch = Regex.Match(whereExpr, @"^(\w+)\s*([<>=!]+)\s*(.+)$");
        if (literalOpMatch.Success)
        {
            var leftCol  = literalOpMatch.Groups[1].Value;
            var op       = literalOpMatch.Groups[2].Value;
            var rightRaw = literalOpMatch.Groups[3].Value.Trim().Trim('\'');
            existingRow.TryGetValue(leftCol, out var leftVal);
            var colIdx   = table.Columns.IndexOf(leftCol);
            var colType  = colIdx >= 0 ? table.ColumnTypes[colIdx] : DataType.String;
            var rightVal = SqlParser.ParseValue(rightRaw, colType);
            return SqlParser.AreValuesEqual(leftVal, rightVal)
                ? op is "=" or "==" or ">=" or "<="
                : ConflictCompareValues(leftVal, rightVal, op);
        }

        return true;
    }

    private static bool ConflictCompareValues(object? left, object? right, string op)
    {
        if (left is null || right is null)
            return false;

        int cmp;
        try   { cmp = Comparer<object>.Default.Compare(left, right); }
        catch { cmp = string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal); }

        return op switch
        {
            "<"          => cmp < 0,
            "<="         => cmp <= 0,
            ">"          => cmp > 0,
            ">="         => cmp >= 0,
            "!=" or "<>" => cmp != 0,
            _            => false,
        };
    }
}
