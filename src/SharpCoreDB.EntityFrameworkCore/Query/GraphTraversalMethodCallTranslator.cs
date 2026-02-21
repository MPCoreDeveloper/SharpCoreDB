// <copyright file="GraphTraversalMethodCallTranslator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Query;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Translates LINQ graph traversal methods to SQL GRAPH_TRAVERSE() function calls.
/// ✅ GraphRAG Phase 2: EF Core query translation for graph operations.
/// 
/// Handles:
/// - .Traverse(startId, relationshipColumn, maxDepth, strategy)
/// - .WhereIn(traversalIds)
/// - .TraverseWhere(startId, relationshipColumn, maxDepth, strategy, predicate)
/// </summary>
public sealed class GraphTraversalMethodCallTranslator(ISqlExpressionFactory sqlExpressionFactory)
    : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory
        ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));

    // Method info references
    private static readonly MethodInfo _traverseMethod =
        typeof(GraphTraversalQueryableExtensions)
            .GetMethods()
            .First(m => m.Name == nameof(GraphTraversalQueryableExtensions.Traverse)
                && m.GetParameters().Length == 5)!;

    private static readonly MethodInfo _whereInMethod =
        typeof(GraphTraversalQueryableExtensions)
            .GetMethod(nameof(GraphTraversalQueryableExtensions.WhereIn), 
                [typeof(IQueryable<>), typeof(IEnumerable<long>)])!;

    private static readonly MethodInfo _traverseWhereMethod =
        typeof(GraphTraversalQueryableExtensions)
            .GetMethod(nameof(GraphTraversalQueryableExtensions.TraverseWhere))!;

    private static readonly MethodInfo _graphTraverseFunction =
        typeof(SharpCoreDBDbFunctionsExtensions)
            .GetMethod(nameof(SharpCoreDBDbFunctionsExtensions.GraphTraverse),
                [typeof(long), typeof(string), typeof(int), typeof(GraphTraversalStrategy)])!;

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);

        // Handle DbFunctions.GraphTraverse(startId, relationshipColumn, maxDepth, strategy)
        if (method == _graphTraverseFunction && arguments.Count == 4)
        {
            var startNodeId = arguments[0];
            var relationshipColumn = arguments[1];
            var maxDepth = arguments[2];
            var strategyArg = arguments[3];

            if (strategyArg is not SqlConstantExpression { Value: GraphTraversalStrategy strategy })
            {
                return null;
            }

            return _sqlExpressionFactory.Function(
                "GRAPH_TRAVERSE",
                arguments: [startNodeId, relationshipColumn, maxDepth, new SqlConstantExpression(System.Linq.Expressions.Expression.Constant((int)strategy), null)],
                nullable: false,
                argumentsPropagateNullability: [false, false, false, false],
                returnType: typeof(long));
        }

        // Handle: .Traverse(startId, relationshipColumn, maxDepth, strategy)
        if (IsGenericMethodMatch(method, _traverseMethod) && arguments.Count == 5)
        {
            var startNodeId = arguments[1];
            var relationshipColumn = arguments[2];
            var maxDepth = arguments[3];
            var strategyArg = arguments[4];

            if (strategyArg is not SqlConstantExpression { Value: GraphTraversalStrategy strategy })
            {
                return null;
            }

            return _sqlExpressionFactory.Function(
                "GRAPH_TRAVERSE",
                arguments: [startNodeId, relationshipColumn, maxDepth, new SqlConstantExpression(System.Linq.Expressions.Expression.Constant((int)strategy), null)],
                nullable: false,
                argumentsPropagateNullability: [false, false, false, false],
                returnType: typeof(IEnumerable<long>));
        }

        // Handle: .WhereIn(traversalIds)
        if (IsGenericMethodMatch(method, _whereInMethod) && arguments.Count == 2)
        {
            return null;
        }

        // Handle: .TraverseWhere(...)
        if (IsGenericMethodMatch(method, _traverseWhereMethod) && arguments.Count == 6)
        {
            var startNodeId = arguments[1];
            var relationshipColumn = arguments[2];
            var maxDepth = arguments[3];
            var strategyArg = arguments[4];

            if (strategyArg is not SqlConstantExpression { Value: GraphTraversalStrategy strategy })
            {
                return null;
            }

            return _sqlExpressionFactory.Function(
                "GRAPH_TRAVERSE",
                arguments: [startNodeId, relationshipColumn, maxDepth, new SqlConstantExpression(System.Linq.Expressions.Expression.Constant((int)strategy), null)],
                nullable: false,
                argumentsPropagateNullability: [false, false, false, false],
                returnType: typeof(IEnumerable<long>));
        }

        return null;
    }

    /// <summary>
    /// Checks if a method matches the generic method definition.
    /// </summary>
    private static bool IsGenericMethodMatch(MethodInfo method, MethodInfo genericDefinition)
    {
        if (!method.IsGenericMethod)
            return false;

        var methodDef = method.GetGenericMethodDefinition();
        return methodDef == genericDefinition;
    }
}
