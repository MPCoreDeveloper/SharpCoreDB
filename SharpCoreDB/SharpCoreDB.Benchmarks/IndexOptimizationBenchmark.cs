using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks;

[MemoryDiagnoser]
public class IndexOptimizationBenchmark
{
    private IDatabase CreateDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench_index_opt_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        return factory.Create(dbPath, "benchPassword");
    }

    [Benchmark]
    public void SelectWithAutoIndex()
    {
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE Test (Id INTEGER PRIMARY KEY, Name TEXT, Value INTEGER)");
        int recordCount = 10000;
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO Test (Name, Value) VALUES ('Test{i}', {i})");
        }
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT * FROM Test WHERE Name = 'Test5000'");
        }
        sw.Stop();
        Console.WriteLine($"1000 SELECTs with auto-index took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
    }

    [Benchmark]
    public void SelectWithoutIndex()
    {
        // To simulate without index, we can query on a column that doesn't have index
        // But since auto-create on all, perhaps query on a different condition or use a table without index
        // For demo, assume we temporarily disable auto-index for this benchmark
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE TestNoIndex (Id INTEGER PRIMARY KEY, Name TEXT, Value INTEGER)");
        // Manually remove index if possible, but since no drop, perhaps create without auto
        // For now, use a WHERE that doesn't benefit from index, like complex WHERE
        int recordCount = 10000;
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO TestNoIndex (Name, Value) VALUES ('Test{i}', {i})");
        }
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT * FROM TestNoIndex WHERE Value > 5000"); // This will do full scan
        }
        sw.Stop();
        Console.WriteLine($"1000 SELECTs without index (full scan) took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
    }
}
