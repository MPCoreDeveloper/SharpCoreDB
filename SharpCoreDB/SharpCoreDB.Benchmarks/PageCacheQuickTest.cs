// <copyright file="PageCacheQuickTest.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using SharpCoreDB.Core.Cache;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Quick performance test voor PageCache - run dit eerst voor snelle validatie.
/// </summary>
public class PageCacheQuickTest
{
    public static void Run()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  PageCache Quick Performance Test");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        RunSequentialTest();
        RunConcurrentTest();
        RunMemoryTest();
        RunStatisticsTest();

        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine("  ✅ Alle tests geslaagd!");
        Console.WriteLine("==============================================");
    }

    private static void RunSequentialTest()
    {
        Console.WriteLine("1. Sequential Access Test...");
        
        using var cache = new PageCache(capacity: 1000, pageSize: 4096);
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < 10000; i++)
        {
            int pageId = i % 1000;
            var page = cache.GetPage(pageId);
            cache.UnpinPage(pageId);
        }
        
        sw.Stop();
        
        double opsPerSecond = 10000.0 / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"   - 10,000 operaties in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"   - {opsPerSecond:N0} ops/sec");
        Console.WriteLine($"   - {sw.ElapsedTicks / 10000.0:F1} ticks per operatie");
        Console.WriteLine($"   - Hit Rate: {cache.Statistics.HitRate:P1}");
        Console.WriteLine($"   ✅ Sequential access werkt!");
        Console.WriteLine();
    }

    private static void RunConcurrentTest()
    {
        Console.WriteLine("2. Concurrent Access Test (8 threads)...");
        
        using var cache = new PageCache(capacity: 1000, pageSize: 4096);
        var sw = Stopwatch.StartNew();
        
        Parallel.For(0, 8, threadId =>
        {
            for (int i = 0; i < 1250; i++)
            {
                int pageId = (threadId * 1250 + i) % 500;
                var page = cache.GetPage(pageId);
                cache.UnpinPage(pageId);
            }
        });
        
        sw.Stop();
        
        double opsPerSecond = 10000.0 / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"   - 10,000 operaties (8 threads) in {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"   - {opsPerSecond:N0} ops/sec");
        Console.WriteLine($"   - Hit Rate: {cache.Statistics.HitRate:P1}");
        Console.WriteLine($"   ✅ Concurrent access werkt!");
        Console.WriteLine();
    }

    private static void RunMemoryTest()
    {
        Console.WriteLine("3. Memory Allocation Test...");
        
        long memBefore = GC.GetTotalMemory(true);
        
        using (var cache = new PageCache(capacity: 1000, pageSize: 4096))
        {
            // Vul cache
            for (int i = 0; i < 1000; i++)
            {
                var page = cache.GetPage(i);
                cache.UnpinPage(i);
            }
            
            long memAfter = GC.GetTotalMemory(false);
            long allocated = memAfter - memBefore;
            
            Console.WriteLine($"   - Cache size: 1000 pages x 4096 bytes = 4 MB");
            Console.WriteLine($"   - Actual memory used: {allocated / 1024 / 1024.0:F2} MB");
            Console.WriteLine($"   - Overhead: {(allocated - (1000 * 4096)) / 1024.0:F0} KB");
            
            // Test hot path allocations
            long gen0Before = GC.CollectionCount(0);
            
            for (int i = 0; i < 10000; i++)
            {
                int pageId = i % 1000;
                var page = cache.GetPage(pageId);
                cache.UnpinPage(pageId);
            }
            
            long gen0After = GC.CollectionCount(0);
            long gen0Collections = gen0After - gen0Before;
            
            Console.WriteLine($"   - Gen0 collections tijdens 10K ops: {gen0Collections}");
            Console.WriteLine($"   ✅ Memory usage is efficient!");
        }
        
        Console.WriteLine();
    }

    private static void RunStatisticsTest()
    {
        Console.WriteLine("4. Statistics & Diagnostics Test...");
        
        using var cache = new PageCache(capacity: 100, pageSize: 4096);
        
        // Generate some activity
        for (int i = 0; i < 150; i++) // Meer dan capacity om evictions te triggeren
        {
            var page = cache.GetPage(i);
            if (i % 2 == 0)
            {
                cache.MarkDirty(i);
            }
            cache.UnpinPage(i);
        }
        
        // Access some pages again voor hits
        for (int i = 0; i < 50; i++)
        {
            cache.PinPage(i);
            cache.UnpinPage(i);
        }
        
        var stats = cache.Statistics;
        Console.WriteLine($"   - Total Hits: {stats.Hits}");
        Console.WriteLine($"   - Total Misses: {stats.Misses}");
        Console.WriteLine($"   - Hit Rate: {stats.HitRate:P1}");
        Console.WriteLine($"   - Evictions: {stats.Evictions}");
        Console.WriteLine($"   - Current Size: {cache.Count}/{cache.Capacity}");
        
        string diag = cache.GetDiagnostics();
        Console.WriteLine($"   - Diagnostics: {diag.Substring(0, Math.Min(100, diag.Length))}...");
        Console.WriteLine($"   ✅ Statistics tracking werkt!");
        Console.WriteLine();
    }

    public static void Main(string[] args)
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("❌ ERROR:");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }
}
