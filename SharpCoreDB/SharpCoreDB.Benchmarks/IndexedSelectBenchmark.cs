using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks;

[MemoryDiagnoser]
public class IndexedSelectBenchmark
{
    private IDatabase CreateDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench_indexed_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        return factory.Create(dbPath, "benchPassword");
    }

    [Benchmark]
    public void IndexedSelect()
    {
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE Test (Id INTEGER PRIMARY KEY, Name TEXT)");
        db.ExecuteSQL("CREATE INDEX idx_name ON Test(Name)");
        int recordCount = 10000;
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO Test (Name) VALUES ('Test{i}')");
        }
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT * FROM Test WHERE Name = 'Test5000'");
        }
        sw.Stop();
        Console.WriteLine($"1000 indexed SELECTs took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
    }
}
