// <copyright file="StorageEngineComparisonTest.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Comprehensive comparison test for storage engine modes.
/// Generates markdown report for README.md
/// </summary>
public class StorageEngineComparisonTest
{
    private const int RecordCount = 10_000;
    private const int RecordSize = 100;

    [Fact]
    public void CompareStorageEngines_GenerateReport()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"storage_comparison_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            // Generate test data
            var testData = GenerateTestData(RecordCount, RecordSize);

            // Test each mode
            var appendMetrics = TestAppendOnlyMode(testDir, testData);
            var pageMetrics = TestPageBasedMode(testDir, testData);
            var hybridMetrics = TestHybridMode(testDir, testData);

            // Generate markdown report
            var report = GenerateMarkdownReport(appendMetrics, pageMetrics, hybridMetrics);

            // Write to file
            var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "STORAGE_ENGINE_COMPARISON.md");
            File.WriteAllText(reportPath, report);

            Console.WriteLine($"? Report generated: {reportPath}");
            Console.WriteLine();
            Console.WriteLine(report);
        }
        finally
        {
            // Force GC to release file handles
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Wait a bit for file handles to close
            System.Threading.Thread.Sleep(500);
            
            // Try cleanup - ignore errors since files might still be locked
            if (Directory.Exists(testDir))
            {
                try
                {
                    Directory.Delete(testDir, recursive: true);
                }
                catch (IOException ex)
                {
                    // Log but don't fail the test - temp files will be cleaned up by OS
                    Console.WriteLine($"??  Could not clean up test directory (files may be locked): {ex.Message}");
                    Console.WriteLine($"    Directory: {testDir}");
                }
                catch
                {
                    // Ignore other cleanup errors
                }
            }
        }
    }

    private static List<byte[]> GenerateTestData(int count, int size)
    {
        var random = new Random(42);
        var data = new List<byte[]>(count);

        for (int i = 0; i < count; i++)
        {
            var record = new byte[size];
            random.NextBytes(record);
            data.Add(record);
        }

        return data;
    }

    private static EngineTestResult TestAppendOnlyMode(string baseDir, List<byte[]> testData)
    {
        var dir = Path.Combine(baseDir, "appendonly");
        Directory.CreateDirectory(dir);

        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Storage(crypto, key, config, null);

        using (var engine = new AppendOnlyEngine(storage, dir))
        {
            return RunTests(engine, "AppendOnly", dir, testData);
        }
    }

    private static EngineTestResult TestPageBasedMode(string baseDir, List<byte[]> testData)
    {
        var dir = Path.Combine(baseDir, "pagebased");
        Directory.CreateDirectory(dir);

        using (var engine = new PageBasedEngine(dir))
        {
            return RunTests(engine, "PageBased", dir, testData);
        }
    }

    private static EngineTestResult TestHybridMode(string baseDir, List<byte[]> testData)
    {
        var dir = Path.Combine(baseDir, "hybrid");
        Directory.CreateDirectory(dir);

        var crypto = new CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Storage(crypto, key, config, null);

        using (var engine = new HybridEngine(storage, dir))
        {
            var result = RunTests(engine, "Hybrid", dir, testData);

            // Add VACUUM test for Hybrid mode
            var vacuumSw = Stopwatch.StartNew();
            var vacuumStats = engine.VacuumAsync().GetAwaiter().GetResult();
            vacuumSw.Stop();

            result.VacuumTimeMs = vacuumSw.ElapsedMilliseconds;
            result.BytesReclaimed = vacuumStats.BytesReclaimed;

            return result;
        }
    }

    private static EngineTestResult RunTests(IStorageEngine engine, string modeName, string dir, List<byte[]> testData)
    {
        var result = new EngineTestResult { ModeName = modeName };

        // Test single inserts - use transaction for efficiency
        engine.BeginTransaction();
        
        var sw = Stopwatch.StartNew();
        var insertedIds = new List<long>();
        for (int i = 0; i < testData.Count; i++)
        {
            insertedIds.Add(engine.Insert("test", testData[i]));
        }
        sw.Stop();
        result.SingleInsertsMs = sw.ElapsedMilliseconds;
        
        engine.CommitAsync().GetAwaiter().GetResult();

        // Test updates - use transaction
        engine.BeginTransaction();
        
        sw.Restart();
        var random = new Random(42);
        for (int i = 0; i < insertedIds.Count; i++)
        {
            var newData = new byte[RecordSize];
            random.NextBytes(newData);
            engine.Update("test", insertedIds[i], newData);
        }
        sw.Stop();
        result.UpdatesMs = sw.ElapsedMilliseconds;
        
        engine.CommitAsync().GetAwaiter().GetResult();

        // Test full scan
        sw.Restart();
        int readCount = 0;
        foreach (var id in insertedIds)
        {
            var data = engine.Read("test", id);
            if (data != null) readCount++;
        }
        sw.Stop();
        result.FullScanMs = sw.ElapsedMilliseconds;
        result.RecordsRead = readCount;

        // Measure file sizes
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        result.TotalFileSize = files.Sum(f => new FileInfo(f).Length);

        // Get performance metrics
        result.Metrics = engine.GetMetrics();

        return result;
    }

    private static string GenerateMarkdownReport(
        EngineTestResult appendOnly,
        EngineTestResult pageBased,
        EngineTestResult hybrid)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Storage Engine Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Test Configuration:** {RecordCount:N0} records × {RecordSize} bytes = {RecordCount * RecordSize / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine();

        // Performance table
        sb.AppendLine("## Performance Comparison");
        sb.AppendLine();
        sb.AppendLine("| Operation | AppendOnly | PageBased | Hybrid | Winner |");
        sb.AppendLine("|-----------|------------|-----------|--------|--------|");

        // Single inserts
        var insertWinner = GetWinner(
            new[] { appendOnly.SingleInsertsMs, pageBased.SingleInsertsMs, hybrid.SingleInsertsMs },
            new[] { "AppendOnly", "PageBased", "Hybrid" });
        sb.AppendLine($"| **10k Single Inserts** | {appendOnly.SingleInsertsMs} ms | {pageBased.SingleInsertsMs} ms | {hybrid.SingleInsertsMs} ms | **{insertWinner}** ? |");

        // Updates
        var updateWinner = GetWinner(
            new[] { appendOnly.UpdatesMs, pageBased.UpdatesMs, hybrid.UpdatesMs },
            new[] { "AppendOnly", "PageBased", "Hybrid" });
        sb.AppendLine($"| **10k Updates** | {appendOnly.UpdatesMs} ms | {pageBased.UpdatesMs} ms | {hybrid.UpdatesMs} ms | **{updateWinner}** ? |");

        // Full scan
        var scanWinner = GetWinner(
            new[] { appendOnly.FullScanMs, pageBased.FullScanMs, hybrid.FullScanMs },
            new[] { "AppendOnly", "PageBased", "Hybrid" });
        sb.AppendLine($"| **Full Scan** | {appendOnly.FullScanMs} ms | {pageBased.FullScanMs} ms | {hybrid.FullScanMs} ms | **{scanWinner}** ? |");

        // VACUUM
        sb.AppendLine($"| **VACUUM Time** | N/A | N/A | {hybrid.VacuumTimeMs} ms | - |");

        sb.AppendLine();

        // File size table
        sb.AppendLine("## File Size Comparison");
        sb.AppendLine();
        sb.AppendLine("| Metric | AppendOnly | PageBased | Hybrid |");
        sb.AppendLine("|--------|------------|-----------|--------|");
        sb.AppendLine($"| **Total Size** | {FormatBytes(appendOnly.TotalFileSize)} | {FormatBytes(pageBased.TotalFileSize)} | {FormatBytes(hybrid.TotalFileSize)} |");

        var baselineSize = RecordCount * RecordSize;
        sb.AppendLine($"| **Overhead** | +{CalcOverhead(appendOnly.TotalFileSize, baselineSize):F1}% | +{CalcOverhead(pageBased.TotalFileSize, baselineSize):F1}% | +{CalcOverhead(hybrid.TotalFileSize, baselineSize):F1}% |");

        if (hybrid.BytesReclaimed > 0)
        {
            sb.AppendLine($"| **Bytes Reclaimed (VACUUM)** | - | - | {FormatBytes(hybrid.BytesReclaimed)} |");
        }

        sb.AppendLine();

        // Detailed metrics
        sb.AppendLine("## Detailed Metrics");
        sb.AppendLine();

        foreach (var result in new[] { appendOnly, pageBased, hybrid })
        {
            sb.AppendLine($"### {result.ModeName}");
            sb.AppendLine();
            sb.AppendLine($"- **Avg Insert Time:** {result.Metrics.AvgInsertTimeMicros:F2} ?s");
            sb.AppendLine($"- **Avg Update Time:** {result.Metrics.AvgUpdateTimeMicros:F2} ?s");
            sb.AppendLine($"- **Avg Read Time:** {result.Metrics.AvgReadTimeMicros:F2} ?s");
            sb.AppendLine($"- **Bytes Written:** {FormatBytes(result.Metrics.BytesWritten)}");
            sb.AppendLine($"- **Bytes Read:** {FormatBytes(result.Metrics.BytesRead)}");

            if (result.Metrics.CustomMetrics.TryGetValue("WalSizeBytes", out var walSize))
            {
                sb.AppendLine($"- **WAL Size:** {FormatBytes(Convert.ToInt64(walSize))}");
            }

            if (result.Metrics.CustomMetrics.TryGetValue("PageSizeBytes", out var pageSize))
            {
                sb.AppendLine($"- **Page File Size:** {FormatBytes(Convert.ToInt64(pageSize))}");
            }

            sb.AppendLine();
        }

        // Pros & Cons
        sb.AppendLine("## Pros & Cons");
        sb.AppendLine();
        sb.AppendLine("### AppendOnly");
        sb.AppendLine("? Fast sequential writes");
        sb.AppendLine("? Simple implementation");
        sb.AppendLine("? Updates create new versions");
        sb.AppendLine("? File grows continuously");
        sb.AppendLine();

        sb.AppendLine("### PageBased");
        sb.AppendLine("? In-place updates");
        sb.AppendLine("? Efficient random reads");
        sb.AppendLine("? No VACUUM needed");
        sb.AppendLine("? Slower writes");
        sb.AppendLine();

        sb.AppendLine("### Hybrid");
        sb.AppendLine("? Fast writes (WAL)");
        sb.AppendLine("? Efficient reads (pages)");
        sb.AppendLine("? Best of both worlds");
        sb.AppendLine("? Background compaction needed");
        sb.AppendLine();

        // Recommendations
        sb.AppendLine("## Recommendations");
        sb.AppendLine();
        sb.AppendLine("| Workload | Use | Reason |");
        sb.AppendLine("|----------|-----|--------|");
        sb.AppendLine("| Write-heavy (logs, events) | **Hybrid** | Fast WAL writes |");
        sb.AppendLine("| Read-heavy (analytics) | **PageBased** | Efficient random access |");
        sb.AppendLine("| Append-only (time-series) | **AppendOnly** | Simplest, fastest |");
        sb.AppendLine("| Mixed OLTP | **Hybrid** | Balanced performance |");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GetWinner(long[] values, string[] names)
    {
        var minIndex = Array.IndexOf(values, values.Min());
        return names[minIndex];
    }

    private static double CalcOverhead(long actualSize, long baselineSize)
    {
        return ((actualSize - baselineSize) / (double)baselineSize) * 100;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F2} MB";
    }

    private class EngineTestResult
    {
        public required string ModeName { get; init; }
        public long SingleInsertsMs { get; set; }
        public long UpdatesMs { get; set; }
        public long FullScanMs { get; set; }
        public long VacuumTimeMs { get; set; }
        public long BytesReclaimed { get; set; }
        public int RecordsRead { get; set; }
        public long TotalFileSize { get; set; }
        public StorageEngineMetrics Metrics { get; set; } = null!;
    }
}
