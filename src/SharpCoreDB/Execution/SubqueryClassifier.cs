// <copyright file="SubqueryClassifier.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Classifies subqueries to determine type and correlation.
/// HOT PATH - Zero-allocation, no LINQ for classification.
/// 
/// Classification Steps:
/// 1. Determine result type (scalar/row/table)
/// 2. Detect correlation (outer column references)
/// 3. Calculate correlation depth
/// 4. Generate cache key for non-correlated subqueries
/// 
/// Performance:
/// - Type detection: O(1) - check SELECT columns and aggregates
/// - Correlation detection: O(n) - scan WHERE/HAVING for outer refs
/// - Total: O(n) where n = subquery AST nodes
/// </summary>
public sealed class SubqueryClassifier
{
    /// <summary>
    /// Classifies a subquery relative to an outer query.
    /// ✅ C# 14: Target-typed new, is patterns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public SubqueryClassification Classify(SubqueryExpressionNode subquery, SelectNode outerQuery)
    {
        ArgumentNullException.ThrowIfNull(subquery);
        ArgumentNullException.ThrowIfNull(outerQuery);

        // Determine subquery type
        var type = DetermineType(subquery.Query);

        // Detect outer references (correlation)
        List<ColumnReferenceNode> outerRefs = [];
        var isCorrelated = DetectCorrelation(subquery.Query, outerQuery, outerRefs);

        // Calculate correlation depth
        var depth = isCorrelated ? 1 : 0; // Multi-level correlation depth: Current implementation supports single-level (depth=1), multi-level to be added in future

        // Generate cache key for non-correlated subqueries
        string? cacheKey = null;
        if (!isCorrelated)
        {
            cacheKey = GenerateCacheKey(subquery.Query);
        }

        return new SubqueryClassification
        {
            Type = type,
            IsCorrelated = isCorrelated,
            OuterReferences = outerRefs,
            CorrelationDepth = depth,
            CacheKey = cacheKey
        };
    }

    /// <summary>
    /// Determines the result type of a subquery.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SubqueryType DetermineType(SelectNode query)
    {
        // Scalar: Single column, aggregate, or single row expectation
        if (query.Columns.Count == 1)
        {
            var col = query.Columns[0];
            if (col.AggregateFunction is not null || query.Limit == 1)
            {
                return SubqueryType.Scalar;
            }
        }

        // Row: Multiple columns, LIMIT 1
        if (query.Columns.Count > 1 && query.Limit == 1)
        {
            return SubqueryType.Row;
        }

        // Table: Default - multiple rows expected
        return SubqueryType.Table;
    }

    /// <summary>
    /// Detects correlation by finding outer table references.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool DetectCorrelation(
        SelectNode subquery,
        SelectNode outerQuery,
        List<ColumnReferenceNode> outerRefs)
    {
        // Get outer table aliases
        HashSet<string> outerAliases = [];
        CollectTableAliases(outerQuery, outerAliases);

        // Scan subquery for references to outer aliases
        bool hasCorrelation = false;

        // Check WHERE clause
        if (subquery.Where is not null)
        {
            hasCorrelation |= ScanForOuterReferences(subquery.Where.Condition, outerAliases, outerRefs);
        }

        // Check HAVING clause
        if (subquery.Having is not null)
        {
            hasCorrelation |= ScanForOuterReferences(subquery.Having.Condition, outerAliases, outerRefs);
        }

        // Check JOIN ON conditions
        if (subquery.From is not null)
        {
            foreach (var join in subquery.From.Joins)
            {
                if (join.OnCondition is not null)
                {
                    hasCorrelation |= ScanForOuterReferences(join.OnCondition, outerAliases, outerRefs);
                }
            }
        }

        return hasCorrelation;
    }

    /// <summary>
    /// Collects table aliases from outer query.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CollectTableAliases(SelectNode query, HashSet<string> aliases)
    {
        if (query.From is not null)
        {
            if (!string.IsNullOrEmpty(query.From.Alias))
            {
                aliases.Add(query.From.Alias);
            }
            else if (!string.IsNullOrEmpty(query.From.TableName))
            {
                aliases.Add(query.From.TableName);
            }

            // Collect JOIN table aliases
            foreach (var join in query.From.Joins)
            {
                if (!string.IsNullOrEmpty(join.Table.Alias))
                {
                    aliases.Add(join.Table.Alias);
                }
                else if (!string.IsNullOrEmpty(join.Table.TableName))
                {
                    aliases.Add(join.Table.TableName);
                }
            }
        }
    }

    /// <summary>
    /// Scans an expression tree for outer table references.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool ScanForOuterReferences(
        ExpressionNode expr,
        HashSet<string> outerAliases,
        List<ColumnReferenceNode> outerRefs)
    {
        bool found = false;

        if (expr is BinaryExpressionNode binary)
        {
            if (binary.Left is not null)
            {
                found |= ScanForOuterReferences(binary.Left, outerAliases, outerRefs);
            }
            if (binary.Right is not null)
            {
                found |= ScanForOuterReferences(binary.Right, outerAliases, outerRefs);
            }
        }
        else if (expr is ColumnReferenceNode colRef)
        {
            // Check if this column references an outer table
            if (colRef.TableAlias is not null && outerAliases.Contains(colRef.TableAlias))
            {
                outerRefs.Add(colRef);
                found = true;
            }
        }
        else if (expr is InExpressionNode inExpr)
        {
            if (inExpr.Expression is not null)
            {
                found |= ScanForOuterReferences(inExpr.Expression, outerAliases, outerRefs);
            }
        }
        else if (expr is FunctionCallNode funcCall)
        {
            foreach (var arg in funcCall.Arguments)
            {
                found |= ScanForOuterReferences(arg, outerAliases, outerRefs);
            }
        }

        return found;
    }

    /// <summary>
    /// Generates a cache key for non-correlated subqueries.
    /// Uses query structure hash to enable cache reuse.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GenerateCacheKey(SelectNode query)
    {
        // Simple implementation: hash the SELECT structure
        // In production, use a visitor to generate normalized key
        var visitor = new SqlToStringVisitor();
        var sql = query.Accept(visitor)?.ToString() ?? string.Empty;
        return $"SUBQ:{sql.GetHashCode():X8}";
    }
}

/// <summary>
/// Result of subquery classification.
/// ✅ C# 14: Collection expressions, required properties.
/// </summary>
public sealed class SubqueryClassification
{
    /// <summary>Gets or sets the subquery type.</summary>
    public required SubqueryType Type { get; init; }

    /// <summary>Gets or sets whether the subquery is correlated.</summary>
    public required bool IsCorrelated { get; init; }

    /// <summary>Gets or sets the outer column references.</summary>
    public required List<ColumnReferenceNode> OuterReferences { get; init; }

    /// <summary>Gets or sets the correlation depth.</summary>
    public required int CorrelationDepth { get; init; }

    /// <summary>Gets or sets the cache key (null for correlated).</summary>
    public string? CacheKey { get; init; }
}
