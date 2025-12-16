// <copyright file="StorageEngineComparisonTest.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

/// <summary>
/// Simple comparison test for AppendOnlyEngine vs PageBasedEngine.
/// Validates correctness and measures performance for 10K inserts.
/// </summary>
public static class StorageEngineComparisonTest
{
    private const int RecordCount = 10_000;
    private const int RecordSize = 100;

    public static void Run()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  SharpCoreDB Storage Engine Comparison - 10K Insert Test     ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        var testDbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        try
        {
            // Generate test data
            Console.WriteLine($"?? Generating {RecordCount:N0} test records ({RecordSize} bytes each)...");
            var testData = GenerateTestData(RecordCount, RecordSize);
            Console.WriteLine("? Test data generated");
            Console.WriteLine();

            // Test AppendOnlyEngine
            Console.WriteLine("??? Testing AppendOnlyEngine ???");
            var appendOnlyResults = TestAppendOnlyEngine(testDbPath, testData);
            Console.WriteLine();

            // Test PageBasedEngine
            Console.WriteLine("??? Testing PageBasedEngine ???");
            var pageBasedResults = TestPageBasedEngine(testDbPath, testData);
            Console.WriteLine();

            // Compare results
            PrintComparison(appendOnlyResults, pageBasedResults);
        }
        finally
        {
            try
            {
                if (Directory.Exists(testDbPath))
                {
                    Directory.Delete(testDbPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
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

    private static TestResults TestAppendOnlyEngine(string basePath, List<byte[]> testData)
    {
        var dbPath = Path.Combine(basePath, "appendonly");
        Directory.CreateDirectory(dbPath);

        var crypto = new Services.CryptoService();
        var key = new byte[32];
        var config = new DatabaseConfig { NoEncryptMode = true };
        var storage = new Services.Storage(crypto, key, config);

        using var engine = new AppendOnlyEngine(storage, dbPath);

        // Test individual inserts
        Console.Write("??  Testing individual inserts... ");
        var sw = Stopwatch.StartNew();
        var references = new List<long>(testData.Count);

        foreach (var data in testData)
        {
            var reference = engine.Insert("test_table", data);
            references.Add(reference);
        }

        sw.Stop();
        var insertTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms");

        // Test reads
        Console.Write("??  Testing reads... ");
        sw.Restart();
        int readSuccessCount = 0;

        foreach (var reference in references)
        {
            var data = engine.Read("test_table", reference);
            if (data != null)
            {
                readSuccessCount++;
            }
        }

        sw.Stop();
        var readTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms ({readSuccessCount:N0}/{testData.Count:N0} successful)");

        // Test transaction mode
        engine.BeginTransaction();
        sw.Restart();

        foreach (var data in testData.Take(1000))
        {
            _ = engine.Insert("test_table", data);
        }

        engine.CommitAsync().GetAwaiter().GetResult();
        sw.Stop();
        var txTime = sw.Elapsed;
        Console.WriteLine($"??  Transaction mode (1K inserts): ? {sw.ElapsedMilliseconds}ms");

        var metrics = engine.GetMetrics();

        return new TestResults
        {
            EngineName = "AppendOnly",
            InsertTime = insertTime,
            ReadTime = readTime,
            TransactionTime = txTime,
            Metrics = metrics
        };
    }

    private static TestResults TestPageBasedEngine(string basePath, List<byte[]> testData)
    {
        var dbPath = Path.Combine(basePath, "pagebased");
        Directory.CreateDirectory(dbPath);

        using var engine = new PageBasedEngine(dbPath);

        // Test individual inserts
        Console.Write("??  Testing individual inserts... ");
        var sw = Stopwatch.StartNew();
        var references = new List<long>(testData.Count);

        foreach (var data in testData)
        {
            var reference = engine.Insert("test_table", data);
            references.Add(reference);
        }

        sw.Stop();
        var insertTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms");

        // Test reads
        Console.Write("??  Testing reads... ");
        sw.Restart();
        int readSuccessCount = 0;

        foreach (var reference in references)
        {
            var data = engine.Read("test_table", reference);
            if (data != null)
            {
                readSuccessCount++;
            }
        }

        sw.Stop();
        var readTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms ({readSuccessCount:N0}/{testData.Count:N0} successful)");

        // Test updates
        Console.Write("??  Testing in-place updates... ");
        sw.Restart();
        var random = new Random(42);

        foreach (var reference in references.Take(1000))
        {
            var newData = new byte[RecordSize];
            random.NextBytes(newData);
            engine.Update("test_table", reference, newData);
        }

        sw.Stop();
        var updateTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms (1K updates)");

        // Test deletes
        Console.Write("??  Testing in-place deletes... ");
        sw.Restart();

        foreach (var reference in references.Take(1000))
        {
            engine.Delete("test_table", reference);
        }

        sw.Stop();
        var deleteTime = sw.Elapsed;
        Console.WriteLine($"? {sw.ElapsedMilliseconds}ms (1K deletes)");

        // Test transaction mode
        engine.BeginTransaction();
        sw.Restart();

        foreach (var data in testData.Take(1000))
        {
            _ = engine.Insert("test_table", data);
        }

        engine.CommitAsync().GetAwaiter().GetResult();
        sw.Stop();
        var txTime = sw.Elapsed;
        Console.WriteLine($"??  Transaction mode (1K inserts): ? {sw.ElapsedMilliseconds}ms");

        var metrics = engine.GetMetrics();

        return new TestResults
        {
            EngineName = "PageBased",
            InsertTime = insertTime,
            ReadTime = readTime,
            UpdateTime = updateTime,
            DeleteTime = deleteTime,
            TransactionTime = txTime,
            Metrics = metrics
        };
    }

    private static void PrintComparison(TestResults appendOnly, TestResults pageBased)
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                    PERFORMANCE COMPARISON                     ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        Console.WriteLine("?? Insert Performance (10K records):");
        Console.WriteLine($"   AppendOnly:  {appendOnly.InsertTime.TotalMilliseconds:F2}ms ({appendOnly.Metrics.AvgInsertTimeMicros:F2}?s/op)");
        Console.WriteLine($"   PageBased:   {pageBased.InsertTime.TotalMilliseconds:F2}ms ({pageBased.Metrics.AvgInsertTimeMicros:F2}?s/op)");
        
        var insertSpeedup = appendOnly.InsertTime.TotalMilliseconds / pageBased.InsertTime.TotalMilliseconds;
        var speedupPercent = (insertSpeedup - 1) * 100;
        
        if (speedupPercent > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ?? PageBased is {speedupPercent:F1}% FASTER");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   ??  PageBased is {Math.Abs(speedupPercent):F1}% slower (unexpected)");
            Console.ResetColor();
        }
        Console.WriteLine();

        Console.WriteLine("?? Read Performance (10K records):");
        Console.WriteLine($"   AppendOnly:  {appendOnly.ReadTime.TotalMilliseconds:F2}ms ({appendOnly.Metrics.AvgReadTimeMicros:F2}?s/op)");
        Console.WriteLine($"   PageBased:   {pageBased.ReadTime.TotalMilliseconds:F2}ms ({pageBased.Metrics.AvgReadTimeMicros:F2}?s/op)");
        Console.WriteLine();

        Console.WriteLine("?? Update Performance (1K records):");
        Console.WriteLine($"   PageBased:   {pageBased.UpdateTime?.TotalMilliseconds:F2}ms ({pageBased.Metrics.AvgUpdateTimeMicros:F2}?s/op)");
        Console.WriteLine($"   ? In-place updates (no append overhead)");
        Console.WriteLine();

        Console.WriteLine("?? Delete Performance (1K records):");
        Console.WriteLine($"   PageBased:   {pageBased.DeleteTime?.TotalMilliseconds:F2}ms ({pageBased.Metrics.AvgDeleteTimeMicros:F2}?s/op)");
        Console.WriteLine($"   ? In-place deletes (no tombstones)");
        Console.WriteLine();

        Console.WriteLine("?? Summary:");
        Console.WriteLine($"   Total Operations: {pageBased.Metrics.TotalInserts + pageBased.Metrics.TotalUpdates + pageBased.Metrics.TotalDeletes + pageBased.Metrics.TotalReads:N0}");
        Console.WriteLine($"   Bytes Written:    {pageBased.Metrics.BytesWritten:N0}");
        Console.WriteLine($"   Bytes Read:       {pageBased.Metrics.BytesRead:N0}");
        Console.WriteLine();

        if (speedupPercent >= 30)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ? TARGET ACHIEVED: PageBased is 30-50% faster for inserts!");
            Console.ResetColor();
        }
        else if (speedupPercent >= 15)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   ??  CLOSE: PageBased is {speedupPercent:F1}% faster (target: 30-50%)");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   ? BELOW TARGET: PageBased is only {speedupPercent:F1}% faster (target: 30-50%)");
            Console.ResetColor();
        }
    }

    private class TestResults
    {
        public string EngineName { get; init; } = string.Empty;
        public TimeSpan InsertTime { get; init; }
        public TimeSpan ReadTime { get; init; }
        public TimeSpan? UpdateTime { get; init; }
        public TimeSpan? DeleteTime { get; init; }
        public TimeSpan TransactionTime { get; init; }
        public StorageEngineMetrics Metrics { get; init; } = new StorageEngineMetrics();
    }
}
