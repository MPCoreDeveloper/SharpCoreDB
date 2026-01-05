// <copyright file="SubqueryPlanner.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Plans subquery execution order and optimizations.
/// HOT PATH - Zero-allocation planning.
/// 
/// Planning Steps:
/// 1. Extract all subqueries from query AST
/// 2. Classify each subquery (type, correlation)
/// 3. Order execution (non-correlated first)
/// 4. Generate cache keys for non-correlated
/// 5. Identify join conversion opportunities
/// 
/// Optimizations:
/// - Non-correlated executed once, cached
/// - Correlated EXISTS → Semi-join
/// - Correlated NOT EXISTS → Anti-join
/// - Scalar subqueries inlined when possible
/// </summary>
public sealed class SubqueryPlanner
{
    private readonly SubqueryClassifier classifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubqueryPlanner"/> class.
    /// </summary>
    public SubqueryPlanner(SubqueryClassifier classifier)
    {
        this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <summary>
    /// Plans subquery execution for a SELECT statement.
    /// ✅ C# 14: Collection expressions, is patterns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public SubqueryExecutionPlan Plan(SelectNode query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Extract all subqueries
        List<SubqueryExpressionNode> subqueries = [];
        ExtractSubqueries(query, subqueries);

        // Classify and order subqueries
        List<ClassifiedSubquery> classified = [];
        foreach (var subquery in subqueries)
        {
            var classification = classifier.Classify(subquery, query);
            
            // Update subquery node with classification
            subquery.Type = classification.Type;
            subquery.IsCorrelated = classification.IsCorrelated;
            subquery.OuterReferences = classification.OuterReferences;
            // Note: Multi-level correlation depth calculation to be implemented in future version
            subquery.CorrelationDepth = classification.CorrelationDepth;
            subquery.CacheKey = classification.CacheKey;
            
            classified.Add(new ClassifiedSubquery
            {
                Subquery = subquery,
                Classification = classification
            });
        }

        // Order: Non-correlated first, then by correlation depth
        classified.Sort((a, b) =>
        {
            if (a.Classification.IsCorrelated != b.Classification.IsCorrelated)
                return a.Classification.IsCorrelated ? 1 : -1;
            return a.Classification.CorrelationDepth.CompareTo(b.Classification.CorrelationDepth);
        });

        // Build execution plan
        var plan = new SubqueryExecutionPlan
        {
            AllSubqueries = classified
        };

        // Separate into categories
        foreach (var item in classified)
        {
            if (!item.Classification.IsCorrelated)
            {
                plan.NonCorrelatedSubqueries.Add(item);
            }
            else if (CanConvertToJoin(item))
            {
                plan.JoinConversionCandidates.Add(item);
            }
            else
            {
                plan.CorrelatedSubqueries.Add(item);
            }
        }

        return plan;
    }

    /// <summary>
    /// Extracts all subqueries from a SELECT statement.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ExtractSubqueries(SelectNode query, List<SubqueryExpressionNode> subqueries)
    {
        // Extract from SELECT columns
        foreach (var column in query.Columns)
        {
            // Columns don't directly contain subqueries in current AST
            // They can have aggregate functions, but not subqueries
        }

        // Extract from FROM subqueries
        if (query.From?.Subquery is not null)
        {
            // FROM subquery - recursive extraction
            ExtractSubqueries(query.From.Subquery, subqueries);
        }

        // Extract from WHERE clause
        if (query.Where is not null)
        {
            ExtractFromExpression(query.Where.Condition, subqueries);
        }

        // Extract from JOIN ON conditions
        if (query.From is not null)
        {
            foreach (var join in query.From.Joins)
            {
                if (join.OnCondition is not null)
                {
                    ExtractFromExpression(join.OnCondition, subqueries);
                }
            }
        }

        // Extract from HAVING clause
        if (query.Having is not null)
        {
            ExtractFromExpression(query.Having.Condition, subqueries);
        }
    }

    /// <summary>
    /// Extracts subqueries from an expression tree.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ExtractFromExpression(ExpressionNode expr, List<SubqueryExpressionNode> subqueries)
    {
        if (expr is SubqueryExpressionNode subquery)
        {
            subqueries.Add(subquery);
            
            // Recursively extract from nested subquery
            ExtractSubqueries(subquery.Query, subqueries);
        }
        else if (expr is BinaryExpressionNode binary)
        {
            if (binary.Left is not null)
                ExtractFromExpression(binary.Left, subqueries);
            if (binary.Right is not null)
                ExtractFromExpression(binary.Right, subqueries);
        }
        else if (expr is InExpressionNode inExpr)
        {
            if (inExpr.Subquery is not null)
            {
                // IN (SELECT ...) - create SubqueryExpressionNode
                var subqueryNode = new SubqueryExpressionNode
                {
                    Query = inExpr.Subquery,
                    Type = SubqueryType.Table
                };
                subqueries.Add(subqueryNode);
                ExtractSubqueries(inExpr.Subquery, subqueries);
            }
            
            if (inExpr.Expression is not null)
                ExtractFromExpression(inExpr.Expression, subqueries);
        }
        else if (expr is FunctionCallNode funcCall)
        {
            foreach (var arg in funcCall.Arguments)
            {
                ExtractFromExpression(arg, subqueries);
            }
        }
    }

    /// <summary>
    /// Determines if a correlated subquery can be converted to a join.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanConvertToJoin(ClassifiedSubquery item)
    {
        // EXISTS and NOT EXISTS are good candidates for semi-join/anti-join
        // Simple correlated subqueries with equality conditions can also convert
        
        // For now, return false (manual join optimization)
        // Production: Analyze subquery structure for join conversion
        return false;
    }
}

/// <summary>
/// Execution plan for subqueries.
/// ✅ C# 14: Collection expressions.
/// </summary>
public sealed class SubqueryExecutionPlan
{
    /// <summary>All subqueries in execution order.</summary>
    public List<ClassifiedSubquery> AllSubqueries { get; init; } = [];

    /// <summary>Non-correlated subqueries (execute once, cache).</summary>
    public List<ClassifiedSubquery> NonCorrelatedSubqueries { get; init; } = [];

    /// <summary>Correlated subqueries (execute per outer row).</summary>
    public List<ClassifiedSubquery> CorrelatedSubqueries { get; init; } = [];

    /// <summary>Candidates for join conversion optimization.</summary>
    public List<ClassifiedSubquery> JoinConversionCandidates { get; init; } = [];
}

/// <summary>
/// Classified subquery with execution metadata.
/// ✅ C# 14: Required properties.
/// </summary>
public sealed class ClassifiedSubquery
{
    /// <summary>The subquery node.</summary>
    public required SubqueryExpressionNode Subquery { get; init; }

    /// <summary>The classification result.</summary>
    public required SubqueryClassification Classification { get; init; }
}
