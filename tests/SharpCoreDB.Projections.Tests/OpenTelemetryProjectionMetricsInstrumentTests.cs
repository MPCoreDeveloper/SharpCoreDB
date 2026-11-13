// <copyright file="OpenTelemetryProjectionMetricsInstrumentTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Projections.Tests;

using System.Diagnostics.Metrics;

/// <summary>
/// Pins the OpenTelemetry contract of <see cref="OpenTelemetryProjectionMetrics"/>: instrument names,
/// units, descriptions, recorded values, tags and the failure branch.
/// Added with the mutation baseline — 34 of 35 mutants in the class survived, because the single
/// existing test asserted only <c>TotalSamples</c>/<c>TotalProcessedEvents</c>. An instrument could be
/// renamed, dropped, or given the wrong unit without a red test, which silently breaks every dashboard
/// and alert built on these names.
/// </summary>
public sealed class OpenTelemetryProjectionMetricsInstrumentTests
{
    private const string Projection = "OrdersProjection";
    private const string Database = "main";
    private const string Tenant = "tenant-a";

    private static ProjectionMetricsSample Sample(
        bool success = true,
        int processedEvents = 5,
        long estimatedLag = 2,
        double durationMs = 12,
        double checkpointAgeMs = 100) =>
        new(
            ProjectionName: Projection,
            DatabaseId: Database,
            TenantId: Tenant,
            ProcessedEvents: processedEvents,
            CheckpointSequence: 42,
            EstimatedLag: estimatedLag,
            CheckpointAge: TimeSpan.FromMilliseconds(checkpointAgeMs),
            Duration: TimeSpan.FromMilliseconds(durationMs),
            Success: success,
            RecordedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void Constants_DescribeThePublishedMeter()
    {
        Assert.Equal("SharpCoreDB.Projections", OpenTelemetryProjectionMetrics.MeterName);
        Assert.Equal("1.5.0", OpenTelemetryProjectionMetrics.InstrumentationVersion);
    }

    [Fact]
    public void Constructor_WithBlankMeterName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OpenTelemetryProjectionMetrics(meterName: "  "));
    }

    [Fact]
    public void Constructor_WithBlankInstrumentationVersion_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OpenTelemetryProjectionMetrics(instrumentationVersion: ""));
    }

    [Fact]
    public void Constructor_PublishesSixInstrumentsWithNameUnitAndDescription()
    {
        // Arrange — the listener must exist before the meter so publication is observed
        using var recorder = new MeterRecorder();

        // Act
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Assert — the exact names, units and descriptions dashboards bind to
        var published = recorder.Instruments.ToDictionary(
            i => i.Name,
            i => (i.Unit, i.Description));

        Assert.Equal(6, published.Count);

        var expected = new Dictionary<string, (string Unit, string Description)>
        {
            ["sharpcoredb.projections.runs.total"] = ("{run}", "Total number of projection runs"),
            ["sharpcoredb.projections.events_processed.total"] = ("{event}", "Total number of events processed by projections"),
            ["sharpcoredb.projections.runs.failed"] = ("{run}", "Total number of failed projection runs"),
            ["sharpcoredb.projections.run.duration_ms"] = ("ms", "Projection run duration in milliseconds"),
            ["sharpcoredb.projections.lag.events"] = ("{event}", "Estimated projection lag in events"),
            ["sharpcoredb.projections.checkpoint.age_ms"] = ("ms", "Projection checkpoint age in milliseconds"),
        };

        var available = string.Join(", ", published.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, unitDescription) in expected)
        {
            Assert.True(published.ContainsKey(name), $"Instrument '{name}' was not published. Published: {available}");
            Assert.Equal(unitDescription, published[name]);
        }
    }

    [Fact]
    public void Record_WhenSuccess_PublishesRunEventsDurationLagAndCheckpointAge()
    {
        // Arrange
        using var recorder = new MeterRecorder();
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Act
        metrics.Record(Sample());

        // Assert — every instrument receives its own value
        Assert.Equal(1, recorder.Value("sharpcoredb.projections.runs.total"));
        Assert.Equal(5, recorder.Value("sharpcoredb.projections.events_processed.total"));
        Assert.Equal(12, recorder.Value("sharpcoredb.projections.run.duration_ms"));
        Assert.Equal(2, recorder.Value("sharpcoredb.projections.lag.events"));
        Assert.Equal(100, recorder.Value("sharpcoredb.projections.checkpoint.age_ms"));
    }

    [Fact]
    public void Record_WhenSuccess_DoesNotCountAFailure()
    {
        // Arrange
        using var recorder = new MeterRecorder();
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Act
        metrics.Record(Sample(success: true));

        // Assert
        Assert.DoesNotContain(recorder.Readings, r => r.Name == "sharpcoredb.projections.runs.failed");
    }

    [Fact]
    public void Record_WhenFailure_CountsOneFailure()
    {
        // Arrange
        using var recorder = new MeterRecorder();
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Act
        metrics.Record(Sample(success: false));

        // Assert
        Assert.Equal(1, recorder.Value("sharpcoredb.projections.runs.failed"));
    }

    [Fact]
    public void Record_TagsIdentifyProjectionDatabaseAndTenant()
    {
        // Arrange
        using var recorder = new MeterRecorder();
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Act
        metrics.Record(Sample());

        // Assert — every reading must carry all three dimensions
        Assert.NotEmpty(recorder.Readings);
        foreach (var reading in recorder.Readings)
        {
            Assert.Equal(Projection, reading.Tag("projection"));
            Assert.Equal(Database, reading.Tag("database"));
            Assert.Equal(Tenant, reading.Tag("tenant"));
        }
    }

    [Fact]
    public void Record_WithBlankProjectionName_ThrowsArgumentException()
    {
        using var metrics = new OpenTelemetryProjectionMetrics();

        Assert.Throws<ArgumentException>(() => metrics.Record(Sample() with { ProjectionName = " " }));
    }

    [Fact]
    public void Record_WithBlankDatabaseId_ThrowsArgumentException()
    {
        using var metrics = new OpenTelemetryProjectionMetrics();

        Assert.Throws<ArgumentException>(() => metrics.Record(Sample() with { DatabaseId = "" }));
    }

    [Fact]
    public void Record_WithBlankTenantId_ThrowsArgumentException()
    {
        using var metrics = new OpenTelemetryProjectionMetrics();

        Assert.Throws<ArgumentException>(() => metrics.Record(Sample() with { TenantId = "  " }));
    }

    [Fact]
    public void Record_WhenCalledTwice_AccumulatesTotals()
    {
        // Arrange
        using var metrics = new OpenTelemetryProjectionMetrics();

        // Act
        metrics.Record(Sample(processedEvents: 5));
        metrics.Record(Sample(processedEvents: 7));

        // Assert
        Assert.Equal(2, metrics.TotalSamples);
        Assert.Equal(12, metrics.TotalProcessedEvents);
    }

    /// <summary>
    /// Captures published instruments and their measurements through <see cref="MeterListener"/>.
    /// </summary>
    private sealed class MeterRecorder : IDisposable
    {
        private readonly MeterListener _listener;

        public MeterRecorder()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == OpenTelemetryProjectionMetrics.MeterName)
                    {
                        Instruments.Add(instrument);
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Readings.Add(new Reading(instrument.Name, value, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Readings.Add(new Reading(instrument.Name, value, tags.ToArray())));
            _listener.Start();
        }

        public List<Instrument> Instruments { get; } = [];

        public List<Reading> Readings { get; } = [];

        public double Value(string instrumentName) =>
            Readings.Single(r => r.Name == instrumentName).Value;

        public void Dispose() => _listener.Dispose();
    }

    private readonly record struct Reading(string Name, double Value, KeyValuePair<string, object?>[] Tags)
    {
        public string? Tag(string key) => Tags.FirstOrDefault(t => t.Key == key).Value as string;
    }
}
