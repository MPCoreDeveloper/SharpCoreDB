// <copyright file="SqlParser.InExpressionSupport.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SqlParser partial class - adds IN expression evaluation support for AstExecutor.
/// ✅ C# 14: Uses collection expressions, modern null patterns, and pattern matching.
/// 
/// This file provides static helper methods that can be called from the AstExecutor nested class
/// to evaluate InExpressionNode in WHERE clauses for queries with JOINs.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Evaluates an IN expression (e.g., column IN (1, 2, 3) or column NOT IN (1, 2, 3)).
    /// Supports value lists. Subqueries are not yet supported.
    /// ✅ C# 14: Uses collection expressions and modern patterns.
    /// </summary>
    /// <param name="inExpr">The IN expression node to evaluate.</param>
    /// <param name="row">The data row to evaluate against.</param>
    /// <returns>True if the expression matches, false otherwise.</returns>
    internal static bool EvaluateInExpression(InExpressionNode inExpr, Dictionary<string, object> row)
    {
        // Get the test value
        if (inExpr.Expression is null)
            return false;

        var testValue = EvaluateExpressionValue(inExpr.Expression, row);
        
        bool matchFound;
        
        // Check if using subquery or value list
        if (inExpr.Subquery is not null)
        {
            // Handle IN (SELECT ...) - would need subquery execution
            throw new NotSupportedException("IN with subquery is not yet supported in AstExecutor. Use value lists for now.");
        }
        else
        {
            // Handle IN (value1, value2, ...)
            // ✅ C# 14: LINQ with modern null handling
            matchFound = inExpr.Values
                .Select(v => EvaluateExpressionValue(v, row))
                .Any(v => AreValuesEqual(testValue, v));
        }
        
        // Apply NOT if needed
        return inExpr.IsNot ? !matchFound : matchFound;
    }

    /// <summary>
    /// Evaluates an expression to get its value (not boolean result).
    /// Used by IN expression evaluation to extract values from AST nodes.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="row">The data row.</param>
    /// <returns>The value of the expression.</returns>
    internal static object? EvaluateExpressionValue(ExpressionNode expression, Dictionary<string, object> row)
    {
        return expression switch
        {
            LiteralNode literal => literal.Value,
            ColumnReferenceNode columnRef => GetColumnValueForExpression(row, columnRef.ColumnName),
            _ => throw new NotSupportedException($"Expression type {expression.GetType()} not supported for value evaluation in IN clause")
        };
    }

    /// <summary>
    /// Gets a column value from a row, handling qualified names (e.g., "table.column").
    /// Used by expression evaluation to resolve column references.
    /// </summary>
    /// <param name="row">The data row.</param>
    /// <param name="columnName">The column name to retrieve.</param>
    /// <returns>The column value, or null if not found.</returns>
    internal static object? GetColumnValueForExpression(Dictionary<string, object> row, string columnName)
    {
        // Try exact match first
        if (row.TryGetValue(columnName, out var value))
            return value;

        // Try to find any key that ends with the column name (for qualified names like "table.column")
        var matchingKey = row.Keys.FirstOrDefault(k => 
            k.EndsWith($".{columnName}", StringComparison.OrdinalIgnoreCase) ||
            k.Equals(columnName, StringComparison.OrdinalIgnoreCase));

        return matchingKey is not null && row.TryGetValue(matchingKey, out value) ? value : null;
    }

    /// <summary>
    /// Compares two values for equality, handling type conversions and nulls.
    /// ✅ COLLATE Phase 3: Now supports collation-aware string comparisons.
    /// ✅ C# 14: Uses pattern matching and modern null checks.
    /// Supports SQL-style comparisons:
    /// - NULL comparisons
    /// - Numeric type conversions (int vs decimal, etc.)
    /// - Collation-aware string comparisons
    /// </summary>
    /// <param name="left">The left value to compare.</param>
    /// <param name="right">The right value to compare.</param>
    /// <param name="collation">Optional collation for string comparisons. Defaults to NoCase for backward compatibility with existing SQL logic.</param>
    /// <returns>True if values are equal, false otherwise.</returns>
    internal static bool AreValuesEqual(object? left, object? right, CollationType collation = CollationType.NoCase)
    {
        // Handle null cases
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        
        // Handle DBNull
        if (left is DBNull && right is DBNull) return true;
        if (left is DBNull || right is DBNull) return false;
        
        // Try direct equality first
        if (Equals(left, right)) return true;
        
        // Try type conversion for numeric types
        try
        {
            if (IsNumericTypeForComparison(left) && IsNumericTypeForComparison(right))
            {
                var leftDecimal = Convert.ToDecimal(left);
                var rightDecimal = Convert.ToDecimal(right);
                return leftDecimal == rightDecimal;
            }
        }
        catch
        {
            // Fall through to string comparison
        }
        
        // ✅ COLLATE Phase 3: Collation-aware string comparison
        return EqualsWithCollation(left.ToString(), right.ToString(), collation);
    }

    /// <summary>
    /// Checks if a value is a numeric type.
    /// ✅ C# 14: Uses pattern matching with 'or' for multiple types.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is numeric, false otherwise.</returns>
    internal static bool IsNumericTypeForComparison(object value)
    {
        return value is int or long or short or byte 
            or uint or ulong or ushort or sbyte
            or float or double or decimal;
    }
}
