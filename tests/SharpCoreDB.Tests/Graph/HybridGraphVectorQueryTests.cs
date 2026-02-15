// <copyright file="HybridGraphVectorQueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Graph;
using SharpCoreDB.Services;

/// <summary>
/// Tests for hybrid graph + vector query optimization.
/// ✅ GraphRAG Phase 3: Query optimization for RAG pipelines.
/// </summary>
public class HybridGraphVectorQueryTests
{
    [Fact]
    public void OptimizeQuery_WithGraphTraversalAndVectorSearch_ReturnsGraphThenVectorOrder()
    {
        // Arrange
        var optimizer = new HybridGraphVectorOptimizer();
        var selectNode = CreateSampleSelectWithGraphAndVector();

        // Act
        var hint = optimizer.OptimizeQuery(selectNode);

        // Assert
        Assert.True(hint.HasGraphTraversal);
        Assert.True(hint.HasVectorSearch);
        Assert.Equal(ExecutionOrder.GraphThenVector, hint.RecommendedOrder);
    }

    [Fact]
    public void OptimizeQuery_WithOnlyGraphTraversal_ReturnsGraphOnly()
    {
        // Arrange
        var optimizer = new HybridGraphVectorOptimizer();
        var selectNode = CreateSampleSelectWithGraphOnly();

        // Act
        var hint = optimizer.OptimizeQuery(selectNode);

        // Assert
        Assert.True(hint.HasGraphTraversal);
        Assert.False(hint.HasVectorSearch);
        Assert.Equal(ExecutionOrder.GraphOnly, hint.RecommendedOrder);
    }

    [Fact]
    public void OptimizeQuery_WithOnlyVectorSearch_ReturnsVectorOnly()
    {
        // Arrange
        var optimizer = new HybridGraphVectorOptimizer();
        var selectNode = CreateSampleSelectWithVectorOnly();

        // Act
        var hint = optimizer.OptimizeQuery(selectNode);

        // Assert
        Assert.False(hint.HasGraphTraversal);
        Assert.True(hint.HasVectorSearch);
        Assert.Equal(ExecutionOrder.VectorOnly, hint.RecommendedOrder);
    }

    private static SelectNode CreateSampleSelectWithGraphAndVector()
    {
        var graphTraverse = new GraphTraverseNode
        {
            TableName = "knowledge_graph",
            StartNode = new LiteralNode { Value = 1L },
            RelationshipColumn = "references",
            MaxDepth = new LiteralNode { Value = 3 },
            Strategy = "BFS"
        };

        var vectorFunc = new FunctionCallNode
        {
            FunctionName = "VEC_DISTANCE_COSINE",
            Arguments =
            [
                new ColumnReferenceNode { ColumnName = "embedding" },
                new LiteralNode { Value = "[0.1, 0.2, 0.3]" }
            ]
        };

        var inExpr = new InExpressionNode
        {
            Expression = new ColumnReferenceNode { ColumnName = "id" },
            IsNot = false
        };

        var whereCondition = new BinaryExpressionNode
        {
            Left = inExpr,
            Operator = "AND",
            Right = new BinaryExpressionNode
            {
                Left = vectorFunc,
                Operator = "<",
                Right = new LiteralNode { Value = 0.2 }
            }
        };

        return new SelectNode
        {
            Columns = [new ColumnNode { Name = "*", IsWildcard = true }],
            From = new FromNode { TableName = "documents" },
            Where = new WhereNode { Condition = whereCondition }
        };
    }

    private static SelectNode CreateSampleSelectWithGraphOnly()
    {
        var graphTraverse = new GraphTraverseNode
        {
            TableName = "knowledge_graph",
            StartNode = new LiteralNode { Value = 1L },
            RelationshipColumn = "references",
            MaxDepth = new LiteralNode { Value = 3 }
        };

        var inExpr = new InExpressionNode
        {
            Expression = new ColumnReferenceNode { ColumnName = "id" }
        };

        return new SelectNode
        {
            Columns = [new ColumnNode { Name = "*", IsWildcard = true }],
            From = new FromNode { TableName = "documents" },
            Where = new WhereNode { Condition = inExpr }
        };
    }

    private static SelectNode CreateSampleSelectWithVectorOnly()
    {
        var vectorFunc = new FunctionCallNode
        {
            FunctionName = "VEC_DISTANCE_COSINE",
            Arguments =
            [
                new ColumnReferenceNode { ColumnName = "embedding" },
                new LiteralNode { Value = "[0.1, 0.2]" }
            ]
        };

        var condition = new BinaryExpressionNode
        {
            Left = vectorFunc,
            Operator = "<",
            Right = new LiteralNode { Value = 0.5 }
        };

        return new SelectNode
        {
            Columns = [new ColumnNode { Name = "*", IsWildcard = true }],
            From = new FromNode { TableName = "documents" },
            Where = new WhereNode { Condition = condition }
        };
    }
}
