using System;
using System.Collections.Generic;
using SharpCoreDB.Interfaces;

#nullable enable

namespace SharpCoreDB.Examples.TimeSeries;

/// <summary>
/// Time-series query patterns for metrics analysis.
/// Uses composite index (MetricType, Timestamp) for sub-millisecond response times.
/// </summary>
public sealed class MetricsAnalytics(IDatabase database)
{
    private readonly IDatabase _database = database;

    /// <summary>
    /// Get average metric value over a time window.
    /// </summary>
    public double GetAverageMetric(string metricType, TimeSpan lookback)
    {
        var rows = _database.ExecuteQuery("""
            SELECT AVG(Value) as AvgValue
            FROM Metrics
            WHERE MetricType = @0 AND Timestamp >= @1
            """,
            new Dictionary<string, object?>
            {
                { "0", metricType },
                { "1", DateTime.UtcNow.Subtract(lookback) }
            });

        return rows.Count > 0 && rows[0]["AvgValue"] != null
            ? (double)rows[0]["AvgValue"]
            : 0d;
    }

    /// <summary>
    /// Get peak metric value with timestamp (performance spike detection).
    /// </summary>
    public (double PeakValue, DateTime PeakTime) GetPeakMetric(string metricType, TimeSpan lookback)
    {
        var rows = _database.ExecuteQuery("""
            SELECT MAX(Value) as PeakValue, Timestamp
            FROM Metrics
            WHERE MetricType = @0 AND Timestamp >= @1
            ORDER BY Value DESC
            LIMIT 1
            """,
            new Dictionary<string, object?>
            {
                { "0", metricType },
                { "1", DateTime.UtcNow.Subtract(lookback) }
            });

        if (rows.Count > 0 && rows[0]["PeakValue"] != null)
        {
            return ((double)rows[0]["PeakValue"], (DateTime)rows[0]["Timestamp"]);
        }

        return (0d, DateTime.UtcNow);
    }

    /// <summary>
    /// Detect anomalies: return metrics exceeding threshold.
    /// </summary>
    public List<MetricSample> DetectAnomalies(string metricType, double threshold, TimeSpan lookback)
    {
        var rows = _database.ExecuteQuery("""
            SELECT Timestamp, MetricType, HostName, Value, Unit
            FROM Metrics
            WHERE MetricType = @0 AND Timestamp >= @1 AND Value > @2
            ORDER BY Id DESC
            LIMIT 500
            """,
            new Dictionary<string, object?>
            {
                { "0", metricType },
                { "1", DateTime.UtcNow.Subtract(lookback) },
                { "2", threshold }
            });

        var results = new List<MetricSample>();
        foreach (var row in rows)
        {
            results.Add(new MetricSample(
                (string)row["MetricType"],
                (double)row["Value"],
                (string)row["Unit"],
                (DateTime)row["Timestamp"],
                (string)row["HostName"]));
        }

        return results;
    }
}
