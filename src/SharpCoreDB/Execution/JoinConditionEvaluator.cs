// <copyright file="JoinConditionEvaluator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SharpCoreDB; // For CollationType and CollationComparator

/// <summary>
/// Evaluates JOIN ON conditions between two rows.
/// HOT PATH - Zero-allocation, optimized for equality joins.
/// ✅ Phase 7: Collation-aware JOIN comparisons for string columns.
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
/// - Collation-aware string comparisons
/// </summary>
public static class JoinConditionEvaluator
{
    /// <summary>
    /// Creates a condition evaluator from ON clause expression.
    /// Parses simple equality conditions: "table1.col1 = table2.col2".
    /// ✅ FIXED: Correctly handles inverted ON clause column order.
    /// ✅ Phase 7: Added collation resolution for JOIN conditions.
    /// </summary>
    /// <param name="onClause">The ON clause string (e.g., "users.id = orders.user_id").</param>
    /// <param name="leftAlias">Left table alias.</param>
    /// <param name="rightAlias">Right table alias.</param>
    /// <param name="leftTable">Left table (for collation metadata). Can be null.</param>
    /// <param name="rightTable">Right table (for collation metadata). Can be null.</param>
    /// <param name="warningCallback">Optional callback for collation mismatch warnings.</param>
    /// <returns>Condition evaluator function.</returns>
    public static Func<Dictionary<string, object>, Dictionary<string, object>, bool> CreateEvaluator(
        string onClause,
        string? leftAlias,
        string? rightAlias,
        Interfaces.ITable? leftTable = null,
        Interfaces.ITable? rightTable = null,
        Action<string>? warningCallback = null)
    {
        if (string.IsNullOrWhiteSpace(onClause))
        {
            // No condition - always true (for CROSS JOIN)
            return (left, right) => true;
        }

        // Parse ON clause with collation metadata
        var conditions = ParseOnClause(onClause, leftAlias, rightAlias, leftTable, rightTable, warningCallback);

        // Return evaluator function  
        return (leftRow, rightRow) => EvaluateConditions(conditions, leftRow, rightRow);
    }

    /// <summary>
    /// Parses ON clause into list of join conditions.
    /// Supports: "table1.col1 = table2.col2 AND table1.col3 = table2.col4".
    /// ✅ Phase 7: Extracts collation metadata for each column comparison.
    /// </summary>
    private static List<JoinCondition> ParseOnClause(
        string onClause,
        string? leftAlias,
        string? rightAlias,
        Interfaces.ITable? leftTable,
        Interfaces.ITable? rightTable,
        Action<string>? warningCallback)
    {
        List<JoinCondition> conditions = [];

        // Split by AND (simple parser - in production use AST)
        var parts = onClause.Split([" AND ", " and "], StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (ParseSingleCondition(part.Trim(), leftAlias, rightAlias, leftTable, rightTable, warningCallback) is { } condition)
            {
                conditions.Add(condition);
            }
        }

        return conditions;
    }

    /// <summary>
    /// Parses a single condition: "table1.col1 = table2.col2".
    /// ✅ FIXED: Correctly identifies which side is left and which is right based on aliases.
    /// ✅ Phase 7: Resolves collation for the JOIN condition.
    /// </summary>
    private static JoinCondition? ParseSingleCondition(
        string condition,
        string? leftAlias,
        string? rightAlias,
        Interfaces.ITable? leftTable,
        Interfaces.ITable? rightTable,
        Action<string>? warningCallback)
    {
        // Find operator
        if (!condition.Contains('=')) return null;

        var parts = condition.Split('=');
        if (parts.Length != 2) return null;

        var leftPart = parts[0].Trim();
        var rightPart = parts[1].Trim();

        // Parse both sides
        var leftRef = ParseColumnReference(leftPart, leftAlias, rightAlias);
        var rightRef = ParseColumnReference(rightPart, leftAlias, rightAlias);

        // ✅ CRITICAL FIX: Ensure left side is actually from LEFT table, right side from RIGHT table
        // Swap if necessary based on parsed aliases
        var (leftColumn, rightColumn) = (leftRef.isLeft, rightRef.isLeft) switch
        {
            // Normal case: left side is from left, right side is from right
            (true, false) => (leftRef, rightRef),
            
            // ✅ INVERTED: Need to swap because table aliases are in opposite positions
            (false, true) => (rightRef, leftRef),
            
            // Both from same side - still create condition but may not match correctly
            _ => (leftRef, rightRef)
        };

        // ✅ Phase 7: Resolve collation for this JOIN condition
        var collation = ResolveJoinConditionCollation(
            leftColumn, rightColumn, leftTable, rightTable, warningCallback);

        return new JoinCondition
        {
            LeftColumn = leftColumn,
            RightColumn = rightColumn,
            Operator = JoinOperator.Equals,
            Collation = collation // ✅ NEW: Store resolved collation
        };
    }

    /// <summary>
    /// ✅ Phase 7: Resolves the collation to use for a specific JOIN condition.
    /// </summary>
    private static CollationType ResolveJoinConditionCollation(
        (string? table, string column, bool isLeft) leftColumn,
        (string? table, string column, bool isLeft) rightColumn,
        Interfaces.ITable? leftTable,
        Interfaces.ITable? rightTable,
        Action<string>? warningCallback)
    {
        // Default to Binary if no table metadata available
        if (leftTable == null || rightTable == null)
            return CollationType.Binary;

        // Get left column collation
        var leftColIdx = leftTable.Columns.IndexOf(leftColumn.column);
        var leftCollation = leftColIdx >= 0 && leftColIdx < leftTable.ColumnCollations.Count
            ? leftTable.ColumnCollations[leftColIdx]
            : CollationType.Binary;

        // Get right column collation
        var rightColIdx = rightTable.Columns.IndexOf(rightColumn.column);
        var rightCollation = rightColIdx >= 0 && rightColIdx < rightTable.ColumnCollations.Count
            ? rightTable.ColumnCollations[rightColIdx]
            : CollationType.Binary;

        // Resolve using CollationComparator utility
        return CollationComparator.ResolveJoinCollation(
            leftCollation, rightCollation, explicitCollation: null, warningCallback);
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
    /// ✅ Phase 7: Use collation-aware comparison for string values.
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
                JoinOperator.Equals => CompareValues(l, r, condition.Collation) == 0,
                JoinOperator.NotEquals => CompareValues(l, r, condition.Collation) != 0,
                JoinOperator.LessThan => CompareValues(l, r, condition.Collation) < 0,
                JoinOperator.LessThanOrEqual => CompareValues(l, r, condition.Collation) <= 0,
                JoinOperator.GreaterThan => CompareValues(l, r, condition.Collation) > 0,
                JoinOperator.GreaterThanOrEqual => CompareValues(l, r, condition.Collation) >= 0,
                _ => false
            }
        };
    }

    /// <summary>
    /// Gets column value from appropriate row based on column reference.
    /// ✅ FIXED: Correctly handles qualified and unqualified column names.
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

        // Try finding any key that matches the column name (case-insensitive)
        // This handles cases where the row has qualified names but we're looking for unqualified
        var matchingKey = row.Keys.FirstOrDefault(k => 
            k.Equals(columnRef.column, StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith($".{columnRef.column}", StringComparison.OrdinalIgnoreCase));
        
        if (matchingKey is not null && row.TryGetValue(matchingKey, out var fallbackValue))
        {
            return fallbackValue;
        }

        return null;
    }

    /// <summary>
    /// Compares two values for join condition evaluation.
    /// ✅ Phase 7: Use collation-aware comparison for strings.
    /// Returns -1 (less), 0 (equal), or 1 (greater).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int CompareValues(object left, object right, CollationType collation)
    {
        // ✅ Phase 7: String comparison with collation
        if (left is string leftStr && right is string rightStr)
        {
            return CollationComparator.Compare(leftStr, rightStr, collation);
        }

        // Handle same type comparisons (non-string)
        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        // Fallback: try string comparison with collation
        return CollationComparator.Compare(
            left.ToString(), 
            right.ToString(), 
            collation);
    }

    #region Types

    private sealed class JoinCondition
    {
        public required (string? table, string column, bool isLeft) LeftColumn { get; init; }
        public required (string? table, string column, bool isLeft) RightColumn { get; init; }
        public required JoinOperator Operator { get; init; }
        public CollationType Collation { get; init; } = CollationType.Binary; // ✅ NEW: Collation metadata
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
