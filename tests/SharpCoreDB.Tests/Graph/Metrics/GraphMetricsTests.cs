// <copyright file="GraphMetricsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph.Metrics;

using SharpCoreDB.Graph.Metrics;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for GraphMetricsCollector.
/// ✅ GraphRAG Phase 6.3: Validates metrics collection, thread safety, and snapshots.
/// </summary>
public class GraphMetricsTests
{
    [Fact]
    public void GraphMetricsCollector_IsDisabledByDefault()
    {
        // Arrange & Act
        var collector = new GraphMetricsCollector();

        // Assert
        Assert.False(collector.IsEnabled);
    }

    [Fact]
    public void GraphMetricsCollector_Enable_StartsCollection()
    {
        // Arrange
        var collector = new GraphMetricsCollector();

        // Act
        collector.Enable();

        // Assert
        Assert.True(collector.IsEnabled);
    }

    [Fact]
    public void GraphMetricsCollector_RecordNodesVisited_UpdatesCount()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordNodesVisited(100);
        collector.RecordNodesVisited(50);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(150, snapshot.TotalNodesVisited);
    }

    [Fact]
    public void GraphMetricsCollector_RecordEdgesTraversed_UpdatesCount()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordEdgesTraversed(200);
        collector.RecordEdgesTraversed(300);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(500, snapshot.TotalEdgesTraversed);
    }

    [Fact]
    public void GraphMetricsCollector_RecordCacheHits_IncrementsCounts()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordCacheHit();
        collector.RecordCacheHit();
        collector.RecordCacheMiss();

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(2, snapshot.CacheHits);
        Assert.Equal(1, snapshot.CacheMisses);
    }

    [Fact]
    public void GraphMetricsCollector_DisableMetrics_NoOverhead()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Disable();

        // Act
        for (int i = 0; i < 10000; i++)
        {
            collector.RecordNodesVisited(1);
            collector.RecordEdgesTraversed(1);
            collector.RecordCacheHit();
        }

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(0, snapshot.TotalNodesVisited); // Nothing recorded when disabled
        Assert.Equal(0, snapshot.TotalEdgesTraversed);
        Assert.Equal(0, snapshot.CacheHits);
    }

    [Fact]
    public void GraphMetricsCollector_Reset_ClearsAllMetrics()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();
        collector.RecordNodesVisited(100);
        collector.RecordEdgesTraversed(50);
        collector.RecordCacheHit();

        // Act
        collector.Reset();

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(0, snapshot.TotalNodesVisited);
        Assert.Equal(0, snapshot.TotalEdgesTraversed);
        Assert.Equal(0, snapshot.CacheHits);
    }

    [Fact]
    public void GraphMetricsCollector_GetSnapshot_IsAtomic()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act - Record some metrics
        collector.RecordNodesVisited(100);
        var snapshot1 = collector.GetSnapshot();

        collector.RecordEdgesTraversed(50);
        var snapshot2 = collector.GetSnapshot();

        // Assert - Snapshots should reflect their respective states
        Assert.Equal(100, snapshot1.TotalNodesVisited);
        Assert.Equal(0, snapshot1.TotalEdgesTraversed);

        Assert.Equal(100, snapshot2.TotalNodesVisited);
        Assert.Equal(50, snapshot2.TotalEdgesTraversed);
    }

    [Fact]
    public void GraphMetricsCollector_ThreadSafety_ConcurrentUpdates()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();
        int threadCount = 10;
        int operationsPerThread = 1000;
        var tasks = new Task[threadCount];

        // Act - Record concurrently
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    collector.RecordNodesVisited(1);
                    collector.RecordEdgesTraversed(1);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(threadCount * operationsPerThread, snapshot.TotalNodesVisited);
        Assert.Equal(threadCount * operationsPerThread, snapshot.TotalEdgesTraversed);
    }

    [Fact]
    public void GraphMetricsCollector_UpdateMaxDepth_TracksMax()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.UpdateMaxDepth(3);
        collector.UpdateMaxDepth(5);
        collector.UpdateMaxDepth(2);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(5, snapshot.MaxDepthReached);
    }

    [Fact]
    public void GraphMetricsCollector_RecordTraversalTime_CalculatesAverageTime()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordTraversalTime(TimeSpan.FromMilliseconds(100));
        collector.RecordTraversalTime(TimeSpan.FromMilliseconds(200));

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(2, snapshot.TraversalCount);
        Assert.Equal(TimeSpan.FromMilliseconds(150), snapshot.AverageExecutionTime);
    }

    [Fact]
    public void GraphMetricsCollector_RecordCacheLookupTime_CalculatesAverage()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordCacheLookupTime(TimeSpan.FromMilliseconds(10));
        collector.RecordCacheLookupTime(TimeSpan.FromMilliseconds(20));
        collector.RecordCacheHit();
        collector.RecordCacheHit();

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(TimeSpan.FromMilliseconds(15), snapshot.AverageLookupTime);
    }

    [Fact]
    public void GraphMetricsCollector_RecordHeuristicEvaluation_TracksCalls()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordHeuristicEvaluation(TimeSpan.FromMilliseconds(5), wasAdmissible: true);
        collector.RecordHeuristicEvaluation(TimeSpan.FromMilliseconds(3), wasAdmissible: true);
        collector.RecordHeuristicEvaluation(TimeSpan.FromMilliseconds(7), wasAdmissible: false);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(3, snapshot.HeuristicCalls);
        Assert.Equal(2, snapshot.AdmissibleEstimates);
        Assert.Equal(1, snapshot.OverEstimates);
    }

    [Fact]
    public void GraphMetricsCollector_RecordOptimizerPrediction_TracksAccuracy()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordOptimizerPrediction(estimatedCostMs: 100, actualCostMs: 100, strategyOverridden: false);
        collector.RecordOptimizerPrediction(estimatedCostMs: 50, actualCostMs: 100, strategyOverridden: true);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(2, snapshot.OptimizerInvocations);
        Assert.Equal(1, snapshot.StrategyOverrides);
        // Error percentage calculation
        Assert.True(snapshot.AveragePredictionError > 0);
    }

    [Fact]
    public void GraphMetricsCollector_Global_IsSingleton()
    {
        // Arrange & Act
        var global1 = GraphMetricsCollector.Global;
        var global2 = GraphMetricsCollector.Global;

        // Assert
        Assert.Same(global1, global2);
    }

    [Fact]
    public void GraphMetricsCollector_ParallelTraversalMetrics()
    {
        // Arrange
        var collector = new GraphMetricsCollector();
        collector.Enable();

        // Act
        collector.RecordParallelTraversal(nodesVisited: 1000, edgesTraversed: 2000, degreeOfParallelism: 8, executionTimeMs: 150);
        collector.RecordParallelTraversal(nodesVisited: 500, edgesTraversed: 1500, degreeOfParallelism: 8, executionTimeMs: 100, workStealingOperations: 42);

        // Assert
        var snapshot = collector.GetSnapshot();
        Assert.Equal(2, snapshot.ParallelTraversals);
        Assert.Equal(1500, snapshot.TotalNodesVisited); // 1000 + 500
        Assert.Equal(3500, snapshot.TotalEdgesTraversed); // 2000 + 1500
    }
}
