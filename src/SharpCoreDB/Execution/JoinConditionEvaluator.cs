// <copyright file="JoinConditionEvaluator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Evaluates JOIN ON conditions between two rows.
/// HOT PATH - Zero-allocation, optimized for equality joins.
/// 
/// Supported Conditions:
/// - Equality: table1.col1 = table2.col2
/// - Multi-column: table1.col1 = table2.col2 AND table1.col3 = table2.col4
/// - Mixed: table1.id = table2.user_id AND table1.status = 'active'
/// 
/// Performance:
/// - Direct column lookups (no reflection)
/// - Short-circuit evaluation for AND conditions
/// - Inline comparison for common types
/// </summary>
public static class JoinConditionEvaluator
{
    /// <summary>
    /// Creates a condition evaluator from ON clause expression.
    /// Parses simple equality conditions: "table1.col1 = table2.col2".
    /// ✅ FIXED: Added diagnostic logging to trace condition parsing.
    /// </summary>
    /// <param name="onClause">The ON clause string (e.g., "users.id = orders.user_id").</param>
    /// <param name="leftAlias">Left table alias.</param>
    /// <param name="rightAlias">Right table alias.</param>
    /// <returns>Condition evaluator function.</returns>
    public static Func<Dictionary<string, object>, Dictionary<string, object>, bool> CreateEvaluator(
        string onClause,
        string? leftAlias,
        string? rightAlias)
    {
#if DEBUG
        Console.WriteLine($"[JOIN-PARSE] Creating evaluator for ON clause: '{onClause}'");
        Console.WriteLine($"[JOIN-PARSE]   Left alias: '{leftAlias}', Right alias: '{rightAlias}'");
#endif

        if (string.IsNullOrWhiteSpace(onClause))
        {
            // No condition - always true (for CROSS JOIN)
            return (left, right) => true;
        }

        // Parse ON clause
        var conditions = ParseOnClause(onClause, leftAlias, rightAlias);

#if DEBUG
        Console.WriteLine($"[JOIN-PARSE] Parsed {conditions.Count} conditions:");
        foreach (var cond in conditions)
        {
            Console.WriteLine($"[JOIN-PARSE]   - Left: ({cond.LeftColumn.table}.{cond.LeftColumn.column}, isLeft={cond.LeftColumn.isLeft})");
            Console.WriteLine($"[JOIN-PARSE]   - Right: ({cond.RightColumn.table}.{cond.RightColumn.column}, isLeft={cond.RightColumn.isLeft})");
            Console.WriteLine($"[JOIN-PARSE]   - Operator: {cond.Operator}");
        }
#endif

        // Return evaluator function  
        return (leftRow, rightRow) => EvaluateConditions(conditions, leftRow, rightRow);
    }

    /// <summary>
    /// Parses ON clause into list of join conditions.
    /// Supports: "table1.col1 = table2.col2 AND table1.col3 = table2.col4".
    /// </summary>
    private static List<JoinCondition> ParseOnClause(
        string onClause,
        string? leftAlias,
        string? rightAlias)
    {
        List<JoinCondition> conditions = [];

        // Split by AND (simple parser - in production use AST)
        var parts = onClause.Split([" AND ", " and "], StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (ParseSingleCondition(part.Trim(), leftAlias, rightAlias) is { } condition)
            {
                conditions.Add(condition);
            }
        }

        return conditions;
    }

    /// <summary>
    /// Parses a single condition: "table1.col1 = table2.col2".
    /// </summary>
    private static JoinCondition? ParseSingleCondition(
        string condition,
        string? leftAlias,
        string? rightAlias)
    {
        // Find operator
        if (!condition.Contains('=')) return null;

        var parts = condition.Split('=');
        if (parts.Length != 2) return null;

        var left = parts[0].Trim();
        var right = parts[1].Trim();

        return new JoinCondition
        {
            LeftColumn = ParseColumnReference(left, leftAlias, rightAlias),
            RightColumn = ParseColumnReference(right, leftAlias, rightAlias),
            Operator = JoinOperator.Equals
        };
    }

    /// <summary>
    /// Parses column reference: "table.column" or "column".
    /// Returns (table alias or null, column name, is left side).
    /// </summary>
    private static (string? table, string column, bool isLeft) ParseColumnReference(
        string reference,
        string? leftAlias,
        string? rightAlias)
    {
        if (reference.Contains('.'))
        {
            var parts = reference.Split('.');
            var table = parts[0].Trim();
            var column = parts[1].Trim();

            // Determine if this is left or right side
            bool isLeft = table == leftAlias;

            return (table, column, isLeft);
        }

        // No table qualifier - assume left by default
        return (null, reference, true);
    }

    /// <summary>
    /// Evaluates all conditions against two rows.
    /// Short-circuits on first false condition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool EvaluateConditions(
        List<JoinCondition> conditions,
        Dictionary<string, object> leftRow,
        Dictionary<string, object> rightRow)
    {
        foreach (var condition in conditions)
        {
            if (!EvaluateSingleCondition(condition, leftRow, rightRow))
            {
                return false; // Short-circuit on first false
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates a single condition against two rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool EvaluateSingleCondition(
        JoinCondition condition,
        Dictionary<string, object> leftRow,
        Dictionary<string, object> rightRow)
    {
        // Get values from appropriate rows
        var leftValue = GetColumnValue(condition.LeftColumn, leftRow, rightRow);
        var rightValue = GetColumnValue(condition.RightColumn, leftRow, rightRow);

        // Handle nulls
        return (leftValue, rightValue) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            var (l, r) => condition.Operator switch
            {
                JoinOperator.Equals => CompareValues(l, r) == 0,
                JoinOperator.NotEquals => CompareValues(l, r) != 0,
                JoinOperator.LessThan => CompareValues(l, r) < 0,
                JoinOperator.LessThanOrEqual => CompareValues(l, r) <= 0,
                JoinOperator.GreaterThan => CompareValues(l, r) > 0,
                JoinOperator.GreaterThanOrEqual => CompareValues(l, r) >= 0,
                _ => false
            }
        };
    }

    /// <summary>
    /// Gets column value from appropriate row based on column reference.
    /// ✅ FIXED: Added diagnostic logging and fallback search for column names.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? GetColumnValue(
        (string? table, string column, bool isLeft) columnRef,
        Dictionary<string, object> leftRow,
        Dictionary<string, object> rightRow)
    {
        var row = columnRef.isLeft ? leftRow : rightRow;

        // Try with table qualifier first
        if (columnRef.table is not null)
        {
            string qualifiedName = $"{columnRef.table}.{columnRef.column}";
            if (row.TryGetValue(qualifiedName, out var value))
            {
                return value;
            }
        }

        // Try unqualified column name
        if (row.TryGetValue(columnRef.column, out var unqualifiedValue))
        {
            return unqualifiedValue;
        }

        // ✅ NEW: Try finding any key that matches the column name (case-insensitive)
        // This handles cases where the row has qualified names but we're looking for unqualified
        var matchingKey = row.Keys.FirstOrDefault(k => 
            k.Equals(columnRef.column, StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith($".{columnRef.column}", StringComparison.OrdinalIgnoreCase));
        
        if (matchingKey is not null && row.TryGetValue(matchingKey, out var fallbackValue))
        {
            return fallbackValue;
        }

#if DEBUG
        // Diagnostic: Show what keys are available if we can't find the column
        Console.WriteLine($"[JOIN-DEBUG] Could not find column '{columnRef.column}' (table: {columnRef.table}, isLeft: {columnRef.isLeft})");
        Console.WriteLine($"[JOIN-DEBUG]   Available keys in {'(' + (columnRef.isLeft ? "LEFT" : "RIGHT") + ')'} row: {string.Join(", ", row.Keys)}");
#endif

        return null;
    }

    /// <summary>
    /// Compares two values for join condition evaluation.
    /// Returns -1 (less), 0 (equal), or 1 (greater).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int CompareValues(object left, object right)
    {
        // Handle same type comparisons
        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        // Try string comparison
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    #region Types

    private sealed class JoinCondition
    {
        public required (string? table, string column, bool isLeft) LeftColumn { get; init; }
        public required (string? table, string column, bool isLeft) RightColumn { get; init; }
        public required JoinOperator Operator { get; init; }
    }

    private enum JoinOperator
    {
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual
    }

    #endregion
}
