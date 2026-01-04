// <copyright file="WhereClauseAnalyzer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Optimizations;

using System;
using System.Linq;
using SharpCoreDB.Services;

/// <summary>
/// Analyzes WHERE clauses to detect patterns suitable for SIMD optimization.
/// Detects simple numeric comparisons: column op value (e.g., "salary > 50000").
/// </summary>
public static class WhereClauseAnalyzer
{
    /// <summary>
    /// Metadata for a parsed WHERE clause.
    /// </summary>
    public sealed class WhereClauseMetadata
    {
        /// <summary>Gets or sets the column name being filtered.</summary>
        public required string ColumnName { get; init; }

        /// <summary>Gets or sets the comparison operator.</summary>
        public required SimdWhereFilter.ComparisonOp Operator { get; init; }

        /// <summary>Gets or sets the comparison value (as string).</summary>
        public required string ValueString { get; init; }

        /// <summary>Gets or sets the column data type.</summary>
        public required DataType ColumnType { get; init; }

        /// <summary>Gets whether this WHERE clause is suitable for SIMD optimization.</summary>
        public bool IsSimdOptimizable =>
            ColumnType == DataType.Integer ||
            ColumnType == DataType.Long ||
            ColumnType == DataType.Real ||
            ColumnType == DataType.Decimal;

        /// <summary>Parses the value as integer.</summary>
        public int GetIntValue() => int.Parse(ValueString);

        /// <summary>Parses the value as long.</summary>
        public long GetLongValue() => long.Parse(ValueString);

        /// <summary>Parses the value as double.</summary>
        public double GetDoubleValue() => double.Parse(ValueString);

        /// <summary>Parses the value as decimal.</summary>
        public decimal GetDecimalValue() => decimal.Parse(ValueString);
    }

    /// <summary>
    /// Tries to parse a simple WHERE clause of the form: column op value.
    /// Examples: "salary > 50000", "age >= 25", "id = 123"
    /// </summary>
    /// <param name="where">The WHERE clause string (without "WHERE" keyword).</param>
    /// <param name="columns">The table column names.</param>
    /// <param name="columnTypes">The table column types.</param>
    /// <param name="metadata">Output: parsed metadata if successful.</param>
    /// <returns>True if the WHERE clause is a simple numeric comparison.</returns>
    public static bool TryParseSimpleNumericWhere(
        string? where,
        System.Collections.Generic.List<string> columns,
        System.Collections.Generic.List<DataType> columnTypes,
        out WhereClauseMetadata? metadata)
    {
        metadata = null;

        if (string.IsNullOrWhiteSpace(where))
            return false;

        // Trim and normalize whitespace
        where = where.Trim();

        // Check for compound conditions (AND, OR) - not supported for SIMD yet
        if (where.Contains(" AND ", StringComparison.OrdinalIgnoreCase) ||
            where.Contains(" OR ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Try to parse: column op value
        // Supported operators: >, <, >=, <=, =, !=, <>
        string[] operators = [">=", "<=", "!=", "<>", ">", "<", "="];
        
        foreach (var op in operators)
        {
            int opIndex = where.IndexOf(op, StringComparison.Ordinal);
            if (opIndex > 0)
            {
                // Split into column and value parts
                string columnPart = where[..opIndex].Trim();
                string valuePart = where[(opIndex + op.Length)..].Trim();

                // Validate column exists
                int columnIndex = columns.IndexOf(columnPart);
                if (columnIndex < 0)
                    continue; // Column not found, try next operator

                var columnType = columnTypes[columnIndex];

                // Check if column type is numeric
                if (columnType != DataType.Integer &&
                    columnType != DataType.Long &&
                    columnType != DataType.Real &&
                    columnType != DataType.Decimal)
                {
                    return false; // Not a numeric column
                }

                // Remove quotes if present
                valuePart = valuePart.Trim('\'', '"');

                // Parse operator
                SimdWhereFilter.ComparisonOp compOp;
                try
                {
                    compOp = SimdWhereFilter.ParseOperator(op);
                }
                catch
                {
                    continue; // Invalid operator, try next
                }

                // Validate value can be parsed
                bool validValue = columnType switch
                {
                    DataType.Integer => int.TryParse(valuePart, out _),
                    DataType.Long => long.TryParse(valuePart, out _),
                    DataType.Real => double.TryParse(valuePart, out _),
                    DataType.Decimal => decimal.TryParse(valuePart, out _),
                    _ => false
                };

                if (!validValue)
                    continue; // Can't parse value, try next operator

                // Success!
                metadata = new WhereClauseMetadata
                {
                    ColumnName = columnPart,
                    Operator = compOp,
                    ValueString = valuePart,
                    ColumnType = columnType
                };

                return true;
            }
        }

        return false; // No valid operator found
    }

    /// <summary>
    /// Checks if a WHERE clause is suitable for SIMD optimization without full parsing.
    /// Fast path to avoid unnecessary parsing overhead.
    /// </summary>
    /// <param name="where">The WHERE clause string.</param>
    /// <returns>True if the WHERE clause might be SIMD-optimizable.</returns>
    public static bool IsLikelySimdOptimizable(string? where)
    {
        if (string.IsNullOrWhiteSpace(where))
            return false;

        // Quick heuristics:
        // 1. No AND/OR (compound conditions not supported yet)
        // 2. Contains numeric comparison operators
        // 3. No LIKE, IN, or other complex patterns

        where = where.ToUpperInvariant();

        if (where.Contains(" AND ") || where.Contains(" OR "))
            return false;

        if (where.Contains("LIKE") || where.Contains(" IN ") || 
            where.Contains("BETWEEN") || where.Contains("IS NULL"))
            return false;

        // Must contain at least one comparison operator
        return where.Contains('>') || where.Contains('<') || where.Contains('=');
    }
}
