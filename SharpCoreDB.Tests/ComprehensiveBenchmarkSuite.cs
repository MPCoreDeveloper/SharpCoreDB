// <copyright file="ComprehensiveBenchmarkSuite.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Services;
using SharpCoreDB.Storage.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Comprehensive benchmark suite addressing critical gaps in testing
/// Tests: Scale, Concurrency, Memory, Real-world workloads
/// </summary>
public class ComprehensiveBenchmarkSuite
{
    private readonly ITestOutputHelper output;

    public ComprehensiveBenchmarkSuite(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact(Skip = "Long-running scale test - run manually")]
    public void ScaleTest_1Million_Records()
    {
        var testDir = CreateTestDirectory();

        try
        {
            output.WriteLine("?????????????????????????????????????????????????????????????????");
            output.WriteLine("?  Scale Test: 1 Million Records                                ?");
            output.WriteLine("?????????????????????????????????????????????????????????????????");

            var crypto = new CryptoService();
            var key = new byte[32];
            var config = new DatabaseConfig { NoEncryptMode = true };
            var storage = new Storage(crypto, key, config, null);

            using (var engine = new HybridEngine(storage, testDir))
            {
                // Test 1M inserts
                var sw = Stopwatch.StartNew();
                engine.BeginTransaction();

                for (int i = 0; i < 1_000_000; i++)
                {
                    var data = new byte[100];
                    Array.Fill(data, (byte)(i % 256));
                    engine.Insert("test", data);

                    if (i % 100_000 == 0)
                    {
                        output.WriteLine($"  Progress: {i:N0} / 1,000,000 ({sw.Elapsed.TotalSeconds:F1}s)");
                    }
                }

                engine.CommitAsync().GetAwaiter().GetResult();
                sw.Stop();

                output.WriteLine("");
                output.WriteLine($"? Inserted 1M records in {sw.Elapsed.TotalSeconds:F1}s");
                output.WriteLine($"   Throughput: {1_000_000 / sw.Elapsed.TotalSeconds:N0} inserts/sec");
                output.WriteLine($"   Avg: {sw.Elapsed.TotalMilliseconds / 1_000_000:F3} ms/insert");

                // Measure file sizes
                var files = Directory.GetFiles(testDir, "*", SearchOption.AllDirectories);
                var totalSize = files.Sum(f => new FileInfo(f).Length);
                output.WriteLine($"   Total size: {totalSize / 1024.0 / 1024.0:F2} MB");

                // Memory usage
                var memoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                output.WriteLine($"   Memory: {memoryMB:F2} MB");
            }
        }
        finally
        {
            CleanupTestDirectory(testDir);
        }
    }

    [Fact(Skip = "Long-running concurrency test - run manually")]
    public void ConcurrencyTest_MultiThreaded_Inserts()
    {
        var testDir = CreateTestDirectory();

        try
        {
            output.WriteLine("?????????????????????????????????????????????????????????????????");
            output.WriteLine("?  Concurrency Test: 10 Threads × 10k Inserts                   ?");
            output.WriteLine("?????????????????????????????????????????????????????????????????");

            var crypto = new CryptoService();
            var key = new byte[32];
            var config = new DatabaseConfig { NoEncryptMode = true };
            var storage = new Storage(crypto, key, config, null);

            using (var engine = new HybridEngine(storage, testDir))
            {
                const int threadCount = 10;
                const int insertsPerThread = 10_000;
                var errors = new List<Exception>();
                var completedThreads = 0;

                var sw = Stopwatch.StartNew();

                Parallel.For(0, threadCount, threadId =>
                {
                    try
                    {
                        engine.BeginTransaction();

                        for (int i = 0; i < insertsPerThread; i++)
                        {
                            var data = new byte[100];
                            Array.Fill(data, (byte)((threadId * insertsPerThread + i) % 256));
                            engine.Insert($"test_thread_{threadId}", data);
                        }

                        engine.CommitAsync().GetAwaiter().GetResult();

                        Interlocked.Increment(ref completedThreads);
                        output.WriteLine($"  Thread {threadId} completed: {insertsPerThread:N0} inserts");
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                        output.WriteLine($"  ? Thread {threadId} failed: {ex.Message}");
                    }
                });

                sw.Stop();

                output.WriteLine("");
                output.WriteLine($"? Completed threads: {completedThreads}/{threadCount}");
                output.WriteLine($"   Total inserts: {completedThreads * insertsPerThread:N0}");
                output.WriteLine($"   Time: {sw.Elapsed.TotalSeconds:F1}s");
                output.WriteLine($"   Throughput: {(completedThreads * insertsPerThread) / sw.Elapsed.TotalSeconds:N0} inserts/sec");

                if (errors.Any())
                {
                    output.WriteLine($"   ? Errors: {errors.Count}");
                    foreach (var error in errors.Take(5))
                    {
                        output.WriteLine($"      {error.GetType().Name}: {error.Message}");
                    }
                }
            }
        }
        finally
        {
            CleanupTestDirectory(testDir);
        }
    }

    [Fact(Skip = "Long-running mixed workload test - run manually")]
    public void MixedWorkload_80Read_20Write()
    {
        var testDir = CreateTestDirectory();

        try
        {
            output.WriteLine("?????????????????????????????????????????????????????????????????");
            output.WriteLine("?  Mixed Workload: 80% Reads, 20% Writes (30 seconds)          ?");
            output.WriteLine("?????????????????????????????????????????????????????????????????");

            var crypto = new CryptoService();
            var key = new byte[32];
            var config = new DatabaseConfig { NoEncryptMode = true };
            var storage = new Storage(crypto, key, config, null);

            using (var engine = new HybridEngine(storage, testDir))
            {
                // Pre-populate with 10k records
                output.WriteLine("  Populating initial dataset (10k records)...");
                engine.BeginTransaction();
                var recordIds = new List<long>();

                for (int i = 0; i < 10_000; i++)
                {
                    var data = new byte[100];
                    Array.Fill(data, (byte)(i % 256));
                    recordIds.Add(engine.Insert("test", data));
                }

                engine.CommitAsync().GetAwaiter().GetResult();
                output.WriteLine("  ? Initial data loaded");

                // Run mixed workload for 30 seconds
                var testDuration = TimeSpan.FromSeconds(30);
                var sw = Stopwatch.StartNew();
                var random = new Random(42);
                long readCount = 0;
                long writeCount = 0;
                var errors = 0;

                while (sw.Elapsed < testDuration)
                {
                    try
                    {
                        if (random.Next(100) < 80)
                        {
                            // 80% reads
                            var id = recordIds[random.Next(recordIds.Count)];
                            var data = engine.Read("test", id);
                            if (data != null) Interlocked.Increment(ref readCount);
                        }
                        else
                        {
                            // 20% writes
                            var data = new byte[100];
                            random.NextBytes(data);
                            
                            if (random.Next(2) == 0)
                            {
                                // Insert
                                var id = engine.Insert("test", data);
                                lock (recordIds)
                                {
                                    recordIds.Add(id);
                                }
                            }
                            else
                            {
                                // Update
                                var id = recordIds[random.Next(recordIds.Count)];
                                engine.Update("test", id, data);
                            }

                            Interlocked.Increment(ref writeCount);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }

                    // Report progress every 5 seconds
                    if (sw.Elapsed.TotalSeconds % 5 < 0.01)
                    {
                        output.WriteLine($"  {sw.Elapsed.TotalSeconds:F0}s: Reads={readCount:N0}, Writes={writeCount:N0}");
                    }
                }

                sw.Stop();

                output.WriteLine("");
                output.WriteLine($"? Completed mixed workload test");
                output.WriteLine($"   Duration: {sw.Elapsed.TotalSeconds:F1}s");
                output.WriteLine($"   Reads: {readCount:N0} ({readCount / sw.Elapsed.TotalSeconds:N0} reads/sec)");
                output.WriteLine($"   Writes: {writeCount:N0} ({writeCount / sw.Elapsed.TotalSeconds:N0} writes/sec)");
                output.WriteLine($"   Total ops: {readCount + writeCount:N0} ({(readCount + writeCount) / sw.Elapsed.TotalSeconds:N0} ops/sec)");
                output.WriteLine($"   Errors: {errors}");

                var metrics = engine.GetMetrics();
                output.WriteLine($"   Avg read: {metrics.AvgReadTimeMicros:F2} ?s");
                output.WriteLine($"   Avg insert: {metrics.AvgInsertTimeMicros:F2} ?s");
                output.WriteLine($"   Avg update: {metrics.AvgUpdateTimeMicros:F2} ?s");
            }
        }
        finally
        {
            CleanupTestDirectory(testDir);
        }
    }

    [Fact(Skip = "Long-running memory test - run manually")]
    public void MemoryTest_LongRunning_LeakDetection()
    {
        var testDir = CreateTestDirectory();

        try
        {
            output.WriteLine("?????????????????????????????????????????????????????????????????");
            output.WriteLine("?  Memory Test: Leak Detection (1M operations)                 ?");
            output.WriteLine("?????????????????????????????????????????????????????????????????");

            var crypto = new CryptoService();
            var key = new byte[32];
            var config = new DatabaseConfig { NoEncryptMode = true };
            var storage = new Storage(crypto, key, config, null);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryBefore = GC.GetTotalMemory(true);
            output.WriteLine($"  Memory before: {memoryBefore / 1024.0 / 1024.0:F2} MB");

            using (var engine = new HybridEngine(storage, testDir))
            {
                engine.BeginTransaction();
                var recordIds = new List<long>();

                for (int i = 0; i < 1_000_000; i++)
                {
                    var data = new byte[100];
                    Array.Fill(data, (byte)(i % 256));
                    var id = engine.Insert("test", data);
                    
                    if (i < 1000) recordIds.Add(id);

                    // Periodic GC to detect leaks
                    if (i % 100_000 == 0 && i > 0)
                    {
                        var memoryNow = GC.GetTotalMemory(false);
                        var growth = (memoryNow - memoryBefore) / 1024.0 / 1024.0;
                        output.WriteLine($"  {i:N0} ops: Memory = {memoryNow / 1024.0 / 1024.0:F2} MB (growth: {growth:F2} MB)");
                    }
                }

                engine.CommitAsync().GetAwaiter().GetResult();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memoryAfter = GC.GetTotalMemory(true);
            var memoryGrowth = (memoryAfter - memoryBefore) / 1024.0 / 1024.0;

            output.WriteLine("");
            output.WriteLine($"? Memory test completed");
            output.WriteLine($"   Memory after: {memoryAfter / 1024.0 / 1024.0:F2} MB");
            output.WriteLine($"   Growth: {memoryGrowth:F2} MB");
            output.WriteLine($"   Growth per operation: {(memoryAfter - memoryBefore) / 1_000_000.0:F2} bytes");

            if (memoryGrowth > 100)
            {
                output.WriteLine($"   ??  WARNING: High memory growth detected!");
            }
            else
            {
                output.WriteLine($"   ? Memory growth within acceptable limits");
            }
        }
        finally
        {
            CleanupTestDirectory(testDir);
        }
    }

    [Fact(Skip = "Long-running large blob test - run manually")]
    public void LargeBlobTest_10MB_Records()
    {
        var testDir = CreateTestDirectory();

        try
        {
            output.WriteLine("?????????????????????????????????????????????????????????????????");
            output.WriteLine("?  Large Blob Test: 10MB Records                                ?");
            output.WriteLine("?????????????????????????????????????????????????????????????????");

            var crypto = new CryptoService();
            var key = new byte[32];
            var config = new DatabaseConfig { NoEncryptMode = true };
            var storage = new Storage(crypto, key, config, null);

            using (var engine = new HybridEngine(storage, testDir))
            {
                // Test 100 × 10MB records
                var blobSize = 10 * 1024 * 1024; // 10MB
                var sw = Stopwatch.StartNew();
                var recordIds = new List<long>();

                engine.BeginTransaction();

                for (int i = 0; i < 100; i++)
                {
                    var data = new byte[blobSize];
                    Array.Fill(data, (byte)(i % 256));
                    
                    var id = engine.Insert("blobs", data);
                    recordIds.Add(id);

                    output.WriteLine($"  Inserted blob {i + 1}/100 ({blobSize / 1024 / 1024}MB)");
                }

                engine.CommitAsync().GetAwaiter().GetResult();
                sw.Stop();

                output.WriteLine("");
                output.WriteLine($"? Inserted 100 × 10MB blobs in {sw.Elapsed.TotalSeconds:F1}s");
                output.WriteLine($"   Throughput: {(100.0 * 10) / sw.Elapsed.TotalSeconds:F2} MB/s");

                // Test reads
                sw.Restart();
                int readSuccess = 0;

                foreach (var id in recordIds)
                {
                    var data = engine.Read("blobs", id);
                    if (data != null && data.Length == blobSize)
                    {
                        readSuccess++;
                    }
                }

                sw.Stop();

                output.WriteLine($"? Read {readSuccess}/100 blobs in {sw.Elapsed.TotalSeconds:F1}s");
                output.WriteLine($"   Throughput: {(readSuccess * 10.0) / sw.Elapsed.TotalSeconds:F2} MB/s");

                // File size
                var files = Directory.GetFiles(testDir, "*", SearchOption.AllDirectories);
                var totalSize = files.Sum(f => new FileInfo(f).Length);
                var overhead = ((totalSize - (100.0 * blobSize)) / (100.0 * blobSize)) * 100;

                output.WriteLine($"   Total size: {totalSize / 1024.0 / 1024.0:F2} MB");
                output.WriteLine($"   Overhead: {overhead:F1}%");
            }
        }
        finally
        {
            CleanupTestDirectory(testDir);
        }
    }

    private static string CreateTestDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"benchmark_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupTestDirectory(string dir)
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(500);

            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
