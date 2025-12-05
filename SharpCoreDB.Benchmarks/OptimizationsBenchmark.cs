using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Diagnostics;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Comprehensive benchmark demonstrating QueryCache, HashIndex, and GC optimizations.
/// Validates the three key optimizations for SharpCoreDB performance.
/// </summary>
public static class OptimizationsBenchmark
{
    public static void RunOptimizationsBenchmark()
    {
        Console.WriteLine("=== SharpCoreDB Optimizations Benchmark (100k records) ===\n");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        
        const int recordCount = 100000;
        
        // ========== Test 1: QueryCache Optimization ==========
        Console.WriteLine("## Test 1: QueryCache Optimization ##");
        TestQueryCache(factory, recordCount);
        Console.WriteLine();
        
        // ========== Test 2: HashIndex Optimization ==========
        Console.WriteLine("## Test 2: HashIndex Optimization ##");
        TestHashIndex(factory, recordCount);
        Console.WriteLine();
        
        // ========== Test 3: Combined Optimizations ==========
        Console.WriteLine("## Test 3: Combined Performance ##");
        TestCombinedPerformance(factory, recordCount);
        Console.WriteLine();
        
        Console.WriteLine("=== Benchmark completed successfully! ===");
    }
    
    private static void TestQueryCache(DatabaseFactory factory, int recordCount)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "bench_querycache");
        if (Directory.Exists(dbPath))
            Directory.Delete(dbPath, true);
        
        // Test with QueryCache enabled
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableQueryCache = true,
            QueryCacheSize = 1000
        };
        var db = factory.Create(dbPath, "benchmarkPassword", false, config);
        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER)");
        
        // Insert data
        Console.Write($"Inserting {recordCount} records with QueryCache... ");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '480')");
        }
        sw.Stop();
        Console.WriteLine($"{sw.ElapsedMilliseconds}ms");
        
        // Test repeated queries (should hit cache)
        Console.Write("Running 1000 repeated SELECT queries (testing cache hit rate)... ");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        sw.Stop();
        var withCacheTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"{withCacheTime}ms");
        
        // Get cache statistics
        var stats = db.GetQueryCacheStatistics();
        Console.WriteLine($"Cache Statistics:");
        Console.WriteLine($"  - Hits: {stats.Hits}");
        Console.WriteLine($"  - Misses: {stats.Misses}");
        Console.WriteLine($"  - Hit Rate: {stats.HitRate:P2}");
        Console.WriteLine($"  - Cached Queries: {stats.Count}");
        
        // Test with QueryCache disabled for comparison
        var dbPathNoCache = Path.Combine(Path.GetTempPath(), "bench_noquerycache");
        if (Directory.Exists(dbPathNoCache))
            Directory.Delete(dbPathNoCache, true);
        
        var configNoCache = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableQueryCache = false
        };
        var dbNoCache = factory.Create(dbPathNoCache, "benchmarkPassword", false, configNoCache);
        dbNoCache.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER)");
        
        for (int i = 0; i < recordCount; i++)
        {
            dbNoCache.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '480')");
        }
        
        Console.Write("Running 1000 repeated SELECT queries (without cache)... ");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            dbNoCache.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        sw.Stop();
        var withoutCacheTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"{withoutCacheTime}ms");
        
        var speedup = (double)withoutCacheTime / withCacheTime;
        Console.WriteLine($"QueryCache Speedup: {speedup:F2}x ({((speedup - 1) * 100):F1}% faster)");
        
        // Cleanup
        if (Directory.Exists(dbPath))
            Directory.Delete(dbPath, true);
        if (Directory.Exists(dbPathNoCache))
            Directory.Delete(dbPathNoCache, true);
    }
    
    private static void TestHashIndex(DatabaseFactory factory, int recordCount)
    {
        // Test with HashIndex
        var dbPathWithIndex = Path.Combine(Path.GetTempPath(), "bench_withindex");
        if (Directory.Exists(dbPathWithIndex))
            Directory.Delete(dbPathWithIndex, true);
        
        var config = DatabaseConfig.HighPerformance;
        var dbWithIndex = factory.Create(dbPathWithIndex, "benchmarkPassword", false, config);
        dbWithIndex.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER)");
        
        Console.Write($"Inserting {recordCount} records... ");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < recordCount; i++)
        {
            dbWithIndex.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '480')");
        }
        sw.Stop();
        Console.WriteLine($"{sw.ElapsedMilliseconds}ms");
        
        // Create hash index
        Console.Write("Creating HashIndex on 'project' column... ");
        sw.Restart();
        dbWithIndex.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");
        sw.Stop();
        Console.WriteLine($"{sw.ElapsedMilliseconds}ms");
        
        // Test SELECT with index
        Console.Write("Running 1000 WHERE queries (with HashIndex)... ");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            dbWithIndex.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        sw.Stop();
        var withIndexTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"{withIndexTime}ms");
        
        // Test without index for comparison
        var dbPathWithoutIndex = Path.Combine(Path.GetTempPath(), "bench_withoutindex");
        if (Directory.Exists(dbPathWithoutIndex))
            Directory.Delete(dbPathWithoutIndex, true);
        
        var configNoIndex = new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableHashIndexes = false  // Disable indexes
        };
        var dbWithoutIndex = factory.Create(dbPathWithoutIndex, "benchmarkPassword", false, configNoIndex);
        dbWithoutIndex.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER)");
        
        for (int i = 0; i < recordCount; i++)
        {
            dbWithoutIndex.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '480')");
        }
        
        Console.Write("Running 1000 WHERE queries (without index, table scan)... ");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            dbWithoutIndex.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        sw.Stop();
        var withoutIndexTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"{withoutIndexTime}ms");
        
        var speedup = (double)withoutIndexTime / withIndexTime;
        Console.WriteLine($"HashIndex Speedup: {speedup:F2}x ({((speedup - 1) * 100):F1}% faster)");
        
        // Cleanup
        if (Directory.Exists(dbPathWithIndex))
            Directory.Delete(dbPathWithIndex, true);
        if (Directory.Exists(dbPathWithoutIndex))
            Directory.Delete(dbPathWithoutIndex, true);
    }
    
    private static void TestCombinedPerformance(DatabaseFactory factory, int recordCount)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "bench_combined");
        if (Directory.Exists(dbPath))
            Directory.Delete(dbPath, true);
        
        // Use HighPerformance config with all optimizations
        var db = factory.Create(dbPath, "benchmarkPassword", false, DatabaseConfig.HighPerformance);
        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER, user TEXT)");
        
        Console.Write($"Inserting {recordCount} records (HighPerformance mode)... ");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '480', 'User{i % 3}')");
        }
        sw.Stop();
        var insertTime = sw.ElapsedMilliseconds;
        Console.WriteLine($"{insertTime}ms ({insertTime / 1000.0:F2}s)");
        
        // Create indexes
        db.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");
        db.ExecuteSQL("CREATE INDEX idx_task ON time_entries (task)");
        
        // Test SELECT performance
        Console.Write("Running 1000 SELECT queries (with all optimizations)... ");
        sw.Restart();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project5'");
        }
        sw.Stop();
        Console.WriteLine($"{sw.ElapsedMilliseconds}ms");
        
        // Test GroupBy
        Console.Write("Running 100 GROUP BY queries... ");
        sw.Restart();
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL("SELECT project, SUM(duration) FROM time_entries GROUP BY project");
        }
        sw.Stop();
        Console.WriteLine($"{sw.ElapsedMilliseconds}ms");
        
        var cacheStats = db.GetQueryCacheStatistics();
        Console.WriteLine($"Final Cache Statistics: {cacheStats.Hits} hits, {cacheStats.HitRate:P2} hit rate");
        
        Console.WriteLine($"\nInsert Performance: {recordCount / (insertTime / 1000.0):F0} records/sec");
        Console.WriteLine($"Target: <130s for 100k inserts (SQLite baseline)");
        Console.WriteLine($"Actual: {insertTime / 1000.0:F1}s for {recordCount} inserts");
        
        if (insertTime < 130000)
        {
            Console.WriteLine($"✓ GOAL ACHIEVED! {(130000.0 / insertTime):F2}x faster than target!");
        }
        else
        {
            Console.WriteLine($"✗ Target not met. Need {((insertTime - 130000) / 1000.0):F1}s improvement.");
        }
        
        // Cleanup
        if (Directory.Exists(dbPath))
            Directory.Delete(dbPath, true);
    }
}
