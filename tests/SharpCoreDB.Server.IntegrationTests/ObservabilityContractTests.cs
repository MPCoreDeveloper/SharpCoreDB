// <copyright file="ObservabilityContractTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Contract tests for diagnostics and observability payloads.
/// Validates server metrics snapshot counters and detailed health triage fields.
/// </summary>
public sealed class ObservabilityContractTests : IAsyncLifetime
{
    private readonly TestServerFixture _fixture = new();

    public async ValueTask InitializeAsync()
        => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public void GetSnapshot_AfterTraffic_ShouldExposeActionableCounters()
    {
        // Arrange
        var metrics = _fixture.GetMetricsCollector();
        var baseline = metrics.GetSnapshot();

        // Act
        metrics.RecordConnectionOpened("binary");
        metrics.RecordProtocolMessage("binary", "Query");
        metrics.RecordSuccessfulRequest("REST/query", latencyMs: 12.5, bytesReceived: 120, bytesSent: 96, rowsReturned: 2);
        metrics.RecordFailedRequest("REST/query", "QUERY_ERROR");
        var snapshot = metrics.GetSnapshot();

        // Assert — use deltas to isolate from fixture initialization traffic
        Assert.Equal(2, snapshot.TotalRequests - baseline.TotalRequests);
        Assert.Equal(1, snapshot.FailedRequests - baseline.FailedRequests);
        Assert.Equal(2, snapshot.QueryRequests - baseline.QueryRequests);
        Assert.Equal(0, snapshot.NonQueryRequests - baseline.NonQueryRequests);
        Assert.Equal("QUERY_ERROR", snapshot.LastFailureCode);
        Assert.True(snapshot.Protocols.ContainsKey("binary"));
        Assert.True(snapshot.Protocols.ContainsKey("rest"));
        Assert.Equal(1, snapshot.Protocols["binary"].TotalMessages);
        Assert.Equal(1, snapshot.Protocols["binary"].TotalConnections);
    }

    [Fact]
    public void GetDetailedHealth_WhenErrorsPresent_ShouldSurfaceTriageSignals()
    {
        // Arrange
        var metrics = _fixture.GetMetricsCollector();
        var healthService = _fixture.GetHealthCheckService();

        // Act
        metrics.RecordFailedRequest("REST/query", "QUERY_ERROR");
        var health = healthService.GetDetailedHealth();

        // Assert
        Assert.Equal("degraded", health.Status);
        Assert.Equal("degraded", health.Checks["request_errors"]);
        Assert.Equal("QUERY_ERROR", health.LastFailureCode);
        Assert.True(health.Protocols.ContainsKey("rest"));
        Assert.True(health.TotalRequests >= 1);
        Assert.True(health.FailedRequests >= 1);
    }
}
