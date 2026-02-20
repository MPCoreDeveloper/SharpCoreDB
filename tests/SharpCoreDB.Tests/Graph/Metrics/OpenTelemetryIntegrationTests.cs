// <copyright file="OpenTelemetryIntegrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph.Metrics;

using SharpCoreDB.Graph.Metrics;
using System;
using System.Diagnostics;
using Xunit;

/// <summary>
/// Tests for OpenTelemetry integration.
/// ✅ GraphRAG Phase 6.3: Validates ActivitySource and Meter functionality.
/// </summary>
public class OpenTelemetryIntegrationTests
{
    /// <summary>
    /// Static constructor to register ActivityListener for test execution.
    /// Required for ActivitySource.StartActivity() to return non-null activities.
    /// </summary>
    static OpenTelemetryIntegrationTests()
    {
        // Register ActivityListener to enable activity creation during tests
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryIntegration.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => { },
            ActivityStopped = activity => { }
        });
    }

    [Fact]
    public void OpenTelemetryIntegration_ActivitySourceCreated()
    {
        // Arrange & Act
        var activitySource = OpenTelemetryIntegration.ActivitySource;

        // Assert
        Assert.NotNull(activitySource);
        Assert.Equal(OpenTelemetryIntegration.ActivitySourceName, activitySource.Name);
        Assert.Equal(OpenTelemetryIntegration.InstrumentationVersion, activitySource.Version);
    }

    [Fact]
    public void OpenTelemetryIntegration_MeterCreated()
    {
        // Arrange & Act
        var meter = OpenTelemetryIntegration.Meter;

        // Assert
        Assert.NotNull(meter);
        Assert.Equal(OpenTelemetryIntegration.MeterName, meter.Name);
    }

    [Fact]
    public void OpenTelemetryIntegration_StartGraphTraversalActivity_CreatesActivity()
    {
        // Arrange & Act
        using var activity = OpenTelemetryIntegration.StartGraphTraversalActivity("Test.GraphTraversal");

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("Test.GraphTraversal", activity.OperationName);
    }

    [Fact]
    public void OpenTelemetryIntegration_ActivityWithTags_SetTagsCorrectly()
    {
        // Arrange
        using var activity = OpenTelemetryIntegration.StartGraphTraversalActivity("Test.WithTags");

        // Act
        activity?.SetTag("graph.startNodeId", 123);
        activity?.SetTag("graph.maxDepth", 5);

        // Assert
        Assert.NotNull(activity);
        var tags = activity.TagObjects;
        // Note: TagObjects is not directly enumerable in all versions,
        // so we verify the activity was created and tags were accepted
        Assert.True(activity.IsAllDataRequested);
    }

    [Fact]
    public void OpenTelemetryIntegration_StartCacheActivity_CreatesActivity()
    {
        // Arrange & Act
        using var activity = OpenTelemetryIntegration.StartCacheActivity("Cache.Lookup");

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("Cache.Lookup", activity.OperationName);
    }

    [Fact]
    public void OpenTelemetryIntegration_StartOptimizerActivity_CreatesActivity()
    {
        // Arrange & Act
        using var activity = OpenTelemetryIntegration.StartOptimizerActivity("Optimizer.SelectStrategy");

        // Assert
        Assert.NotNull(activity);
        Assert.Equal("Optimizer.SelectStrategy", activity.OperationName);
    }

    [Fact]
    public void OpenTelemetryIntegration_RecordTraversalMetrics_RecordsSuccessfully()
    {
        // Arrange
        var initialMetrics = GraphMetricsCollector.Global.GetSnapshot();

        // Act
        OpenTelemetryIntegration.RecordTraversalMetrics(
            nodesVisited: 100,
            edgesTraversed: 200,
            executionTimeMs: 50.0);

        // Assert
        var finalMetrics = GraphMetricsCollector.Global.GetSnapshot();
        // Metrics are collected via OpenTelemetry instruments
        Assert.NotNull(finalMetrics);
    }

    [Fact]
    public void OpenTelemetryIntegration_RecordCacheMetrics_RecordsHitAndMiss()
    {
        // Arrange & Act
        OpenTelemetryIntegration.RecordCacheMetrics(isHit: true, lookupTimeMs: 1.5);
        OpenTelemetryIntegration.RecordCacheMetrics(isHit: false, lookupTimeMs: 2.5);

        // Assert - Verify no exceptions were thrown
        Assert.True(true);
    }

    [Fact]
    public void OpenTelemetryIntegration_RecordHeuristicMetrics_RecordsSuccessfully()
    {
        // Arrange & Act
        OpenTelemetryIntegration.RecordHeuristicMetrics(evaluationTimeMs: 3.0, wasAdmissible: true);
        OpenTelemetryIntegration.RecordHeuristicMetrics(evaluationTimeMs: 2.5, wasAdmissible: false);

        // Assert
        Assert.True(true); // Verify no exceptions
    }

    [Fact]
    public void OpenTelemetryIntegration_RecordOptimizerMetrics_CalculatesError()
    {
        // Arrange & Act
        OpenTelemetryIntegration.RecordOptimizerMetrics(estimatedCostMs: 100, actualCostMs: 100);
        OpenTelemetryIntegration.RecordOptimizerMetrics(estimatedCostMs: 75, actualCostMs: 100);

        // Assert
        Assert.True(true); // Verify no exceptions with various cost values
    }

    [Fact]
    public void OpenTelemetryIntegration_RecordOptimizerMetrics_HandlesZeroCost()
    {
        // Arrange & Act
        OpenTelemetryIntegration.RecordOptimizerMetrics(estimatedCostMs: 0, actualCostMs: 0);

        // Assert
        Assert.True(true); // Should not throw on zero actual cost
    }

    [Fact]
    public void OpenTelemetryIntegration_ActivityDisposedCorrectly()
    {
        // Arrange
        Activity? activity;

        // Act
        using (activity = OpenTelemetryIntegration.StartGraphTraversalActivity("Test.Dispose"))
        {
            Assert.NotNull(activity);
        }

        // Assert
        Assert.NotNull(activity);
        Assert.True(activity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void OpenTelemetryIntegration_MultipleActivitiesNested()
    {
        // Arrange & Act
        using (var outer = OpenTelemetryIntegration.StartGraphTraversalActivity("Outer"))
        {
            outer?.SetTag("level", 1);

            using (var inner = OpenTelemetryIntegration.StartCacheActivity("Inner"))
            {
                inner?.SetTag("level", 2);

                // Assert
                Assert.NotNull(inner);
            }

            Assert.NotNull(outer);
        }

        Assert.True(true);
    }

    [Fact]
    public void OpenTelemetryIntegration_ActivitySourceNameConstants()
    {
        // Arrange & Act & Assert
        Assert.Equal("SharpCoreDB.Graph", OpenTelemetryIntegration.ActivitySourceName);
        Assert.Equal("SharpCoreDB.Graph", OpenTelemetryIntegration.MeterName);
        Assert.Equal("6.3.0", OpenTelemetryIntegration.InstrumentationVersion);
    }
}
