// <copyright file="StorageMetricsCollector.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks.Infrastructure;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Collects comprehensive metrics for storage engine benchmarks including file sizes,
/// performance statistics, and generates comparison reports.
/// </summary>
public class StorageMetricsCollector
{
    private readonly Dictionary<string, EngineMetrics> engineMetrics = new();

    /// <summary>
    /// Collects metrics for a storage engine mode.
    /// </summary>
    public void CollectMetrics(string modeName, string databasePath, StorageEngineMetrics performanceMetrics)
    {
        var metrics = new EngineMetrics
        {
            ModeName = modeName,
            Performance = performanceMetrics,
            FileSizes = MeasureFileSizes(databasePath),
            CollectedAt = DateTime.UtcNow
        };

        engineMetrics[modeName] = metrics;
    }

    /// <summary>
    /// Measures file sizes for all database files in the directory.
    /// </summary>
    private static FileSizeMetrics MeasureFileSizes(string databasePath)
    {
        if (!Directory.Exists(databasePath))
        {
            return new FileSizeMetrics();
        }

        var dataFiles = Directory.GetFiles(databasePath, "*.data", SearchOption.AllDirectories);
        var walFiles = Directory.GetFiles(databasePath, "*.wal", SearchOption.AllDirectories);
        var pageFiles = Directory.GetFiles(databasePath, "*.pages", SearchOption.AllDirectories);
        var allFiles = Directory.GetFiles(databasePath, "*", SearchOption.AllDirectories);

        long dataSize = dataFiles.Sum(f => new FileInfo(f).Length);
        long walSize = walFiles.Sum(f => new FileInfo(f).Length);
        long pageSize = pageFiles.Sum(f => new FileInfo(f).Length);
        long totalSize = allFiles.Sum(f => new FileInfo(f).Length);

        return new FileSizeMetrics
        {
            DataFileBytes = dataSize,
            WalFileBytes = walSize,
            PageFileBytes = pageSize,
            TotalBytes = totalSize,
            FileCount = allFiles.Length
        };
    }

    /// <summary>
    /// Generates a markdown comparison table for README.
    /// </summary>
    public string GenerateMarkdownComparison(int recordCount, int recordSize)
    {
        if (engineMetrics.Count == 0)
        {
            return "?? No metrics collected yet. Run benchmarks first.";
        }

        var sb = new System.Text.StringBuilder();

        sb.AppendLine("## Storage Engine Benchmark Results");
        sb.AppendLine();
        sb.AppendLine($"**Test Configuration:**");
        sb.AppendLine($"- Records: {recordCount:N0}");
        sb.AppendLine($"- Record Size: {recordSize:N0} bytes");
        sb.AppendLine($"- Total Data: {(recordCount * recordSize / 1024.0 / 1024.0):F2} MB");
        sb.AppendLine($"- Test Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // Performance comparison table
        sb.AppendLine("### Performance Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | AppendOnly | PageBased | Hybrid | Winner |");
        sb.AppendLine("|--------|------------|-----------|--------|--------|");

        var modes = new[] { "AppendOnly", "PageBased", "Hybrid" };

        // Insert performance
        sb.Append("| **Insert Time (?s)** |");
        var insertTimes = modes.Select(m => engineMetrics.TryGetValue(m, out var met) ? met.Performance.AvgInsertTimeMicros : 0).ToArray();
        var insertWinner = GetWinner(modes, insertTimes, lowerIsBetter: true);
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                var value = met.Performance.AvgInsertTimeMicros;
                var mark = m == insertWinner ? " ?" : "";
                sb.Append($" {value:F2}{mark} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine($" **{insertWinner}** |");

        // Update performance
        sb.Append("| **Update Time (?s)** |");
        var updateTimes = modes.Select(m => engineMetrics.TryGetValue(m, out var met) ? met.Performance.AvgUpdateTimeMicros : 0).ToArray();
        var updateWinner = GetWinner(modes, updateTimes, lowerIsBetter: true);
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                var value = met.Performance.AvgUpdateTimeMicros;
                var mark = m == updateWinner ? " ?" : "";
                sb.Append($" {value:F2}{mark} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine($" **{updateWinner}** |");

        // Read performance
        sb.Append("| **Read Time (?s)** |");
        var readTimes = modes.Select(m => engineMetrics.TryGetValue(m, out var met) ? met.Performance.AvgReadTimeMicros : 0).ToArray();
        var readWinner = GetWinner(modes, readTimes, lowerIsBetter: true);
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                var value = met.Performance.AvgReadTimeMicros;
                var mark = m == readWinner ? " ?" : "";
                sb.Append($" {value:F2}{mark} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine($" **{readWinner}** |");

        sb.AppendLine();

        // File size comparison
        sb.AppendLine("### File Size Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | AppendOnly | PageBased | Hybrid |");
        sb.AppendLine("|--------|------------|-----------|--------|");

        // Data file size
        sb.Append("| **Data File** |");
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                sb.Append($" {FormatBytes(met.FileSizes.DataFileBytes)} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine();

        // WAL file size
        sb.Append("| **WAL File** |");
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                sb.Append($" {FormatBytes(met.FileSizes.WalFileBytes)} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine();

        // Page file size
        sb.Append("| **Page File** |");
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                sb.Append($" {FormatBytes(met.FileSizes.PageFileBytes)} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine();

        // Total size
        sb.Append("| **Total Size** |");
        var totalSizes = modes.Select(m => engineMetrics.TryGetValue(m, out var met) ? (double)met.FileSizes.TotalBytes : 0).ToArray();
        var sizeWinner = GetWinner(modes, totalSizes, lowerIsBetter: true);
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                var mark = m == sizeWinner ? " ?" : "";
                sb.Append($" {FormatBytes(met.FileSizes.TotalBytes)}{mark} |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine();

        // Space efficiency
        sb.Append("| **Space Efficiency** |");
        var baselineSize = recordCount * recordSize;
        foreach (var m in modes)
        {
            if (engineMetrics.TryGetValue(m, out var met))
            {
                var overhead = ((met.FileSizes.TotalBytes - baselineSize) / (double)baselineSize) * 100;
                sb.Append($" +{overhead:F1}% |");
            }
            else
            {
                sb.Append(" - |");
            }
        }
        sb.AppendLine();

        sb.AppendLine();

        // Add pros & cons and usage recommendations
        GenerateRecommendations(sb);

        return sb.ToString();
    }

    private void GenerateRecommendations(System.Text.StringBuilder sb)
    {
        sb.AppendLine("### Storage Mode Pros & Cons");
        sb.AppendLine();

        sb.AppendLine("#### ? AppendOnly");
        sb.AppendLine("**Pros:**");
        sb.AppendLine("- Fast sequential writes");
        sb.AppendLine("- Simple implementation");
        sb.AppendLine("- Excellent for write-heavy workloads");
        sb.AppendLine();
        sb.AppendLine("**Cons:**");
        sb.AppendLine("- Updates create new versions (write amplification)");
        sb.AppendLine("- File grows continuously");
        sb.AppendLine("- Requires periodic VACUUM");
        sb.AppendLine();

        sb.AppendLine("#### ? PageBased");
        sb.AppendLine("**Pros:**");
        sb.AppendLine("- In-place updates (no write amplification)");
        sb.AppendLine("- Efficient random reads");
        sb.AppendLine("- No VACUUM needed");
        sb.AppendLine("- Predictable file size");
        sb.AppendLine();
        sb.AppendLine("**Cons:**");
        sb.AppendLine("- Slower writes (page management overhead)");
        sb.AppendLine("- Page fragmentation possible");
        sb.AppendLine("- More complex implementation");
        sb.AppendLine();

        sb.AppendLine("#### ? Hybrid (WAL + Pages)");
        sb.AppendLine("**Pros:**");
        sb.AppendLine("- Fast writes via WAL append");
        sb.AppendLine("- Efficient reads after compaction");
        sb.AppendLine("- Best of both worlds for mixed workloads");
        sb.AppendLine("- Automatic background compaction");
        sb.AppendLine();
        sb.AppendLine("**Cons:**");
        sb.AppendLine("- Requires periodic compaction");
        sb.AppendLine("- Higher memory usage (dual indexes)");
        sb.AppendLine("- More complex implementation");
        sb.AppendLine();

        sb.AppendLine("### When to Use Each Mode");
        sb.AppendLine();
        sb.AppendLine("| Workload Type | Recommended | Reason |");
        sb.AppendLine("|--------------|-------------|--------|");
        sb.AppendLine("| **Write-heavy** (logging, events) | **Hybrid** or **AppendOnly** | Fast WAL writes, periodic compaction |");
        sb.AppendLine("| **Read-heavy** (analytics, reporting) | **PageBased** | Efficient random access, no WAL overhead |");
        sb.AppendLine("| **Append-only** (time-series, logs) | **AppendOnly** | Simplest, fastest sequential writes |");
        sb.AppendLine("| **Mixed OLTP** (e-commerce, SaaS) | **Hybrid** | Balances write and read performance |");
        sb.AppendLine("| **Update-heavy** (user profiles) | **PageBased** | In-place updates, no write amplification |");
        sb.AppendLine();
    }

    private static string GetWinner(string[] modes, double[] values, bool lowerIsBetter)
    {
        if (values.All(v => v == 0)) return "N/A";

        var validValues = values.Where(v => v > 0).ToArray();
        if (validValues.Length == 0) return "N/A";

        var winningValue = lowerIsBetter ? validValues.Min() : validValues.Max();
        var winningIndex = Array.IndexOf(values, winningValue);

        return winningIndex >= 0 && winningIndex < modes.Length ? modes[winningIndex] : "N/A";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F2} MB";
    }

    private class EngineMetrics
    {
        public required string ModeName { get; init; }
        public required StorageEngineMetrics Performance { get; init; }
        public required FileSizeMetrics FileSizes { get; init; }
        public required DateTime CollectedAt { get; init; }
    }

    private class FileSizeMetrics
    {
        public long DataFileBytes { get; init; }
        public long WalFileBytes { get; init; }
        public long PageFileBytes { get; init; }
        public long TotalBytes { get; init; }
        public int FileCount { get; init; }
    }
}
