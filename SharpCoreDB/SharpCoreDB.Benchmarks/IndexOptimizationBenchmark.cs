using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using SharpCoreDB.Interfaces;
using System.Threading.Tasks;
using SharpCoreDB.Services;

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

    private (IDatabase, DatabasePool) CreatePooledDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench_concurrent_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        var db = factory.Create(dbPath, "benchPassword", config: DatabaseConfig.HighPerformance);
        var pool = new DatabasePool(provider);
        return (db, pool);
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
    public void SelectWithAutoIndexNoEncrypt()
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
            db.ExecuteQuery("SELECT * FROM Test WHERE Name = 'Test5000'", null, true);
        }
        sw.Stop();
        Console.WriteLine($"1000 SELECTs with auto-index (no encrypt) took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
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

    [Benchmark]
    public void ConcurrentSelectWithJoin()
    {
        var (db, pool) = CreatePooledDatabase();
        try
        {
            // Setup tables with JOIN data
            db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT, DeptId INTEGER)");
            db.ExecuteSQL("CREATE TABLE Departments (Id INTEGER PRIMARY KEY, Name TEXT)");
            
            for (int i = 0; i < 1000; i++)
            {
                db.ExecuteSQL($"INSERT INTO Departments (Name) VALUES ('Dept{i}')");
                for (int j = 0; j < 10; j++)
                {
                    db.ExecuteSQL($"INSERT INTO Users (Name, DeptId) VALUES ('User{i}_{j}', {i + 1})");
                }
            }

            var tasks = new List<Task>();
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < 1000; i++)
            {
                var localI = i;
                tasks.Add(Task.Run(() =>
                {
                    var dbInstance = pool.GetDatabase(db.DbPath, "benchPassword");
                    try
                    {
                        dbInstance.ExecuteSQL("SELECT u.Name, d.Name FROM Users u JOIN Departments d ON u.DeptId = d.Id WHERE u.Id = 1");
                    }
                    finally
                    {
                        pool.ReturnDatabase(dbInstance);
                    }
                }));
            }
            
            Task.WhenAll(tasks).Wait();
            sw.Stop();
            Console.WriteLine($"1000 concurrent SELECT with JOIN took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
        }
        finally
        {
            pool.Dispose();
        }
    }

    [Benchmark]
    public void SingleThreadSelectWithJoin()
    {
        using var db = CreateDatabase();
        // Setup tables with JOIN data
        db.ExecuteSQL("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT, DeptId INTEGER)");
        db.ExecuteSQL("CREATE TABLE Departments (Id INTEGER PRIMARY KEY, Name TEXT)");
        
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL($"INSERT INTO Departments (Name) VALUES ('Dept{i}')");
            for (int j = 0; j < 10; j++)
            {
                db.ExecuteSQL($"INSERT INTO Users (Name, DeptId) VALUES ('User{i}_{j}', {i + 1})");
            }
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("SELECT u.Name, d.Name FROM Users u JOIN Departments d ON u.DeptId = d.Id WHERE u.Id = 1");
        }
        sw.Stop();
        Console.WriteLine($"1000 single-thread SELECT with JOIN took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 1000.0:F3} ms per query");
    }

    [Benchmark]
    public void FullTableScan1000Records()
    {
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE TestScan (Id INTEGER PRIMARY KEY, Name TEXT, Value INTEGER)");
        int recordCount = 1000;
        for (int i = 0; i < recordCount; i++)
        {
            db.ExecuteSQL($"INSERT INTO TestScan (Name, Value) VALUES ('Test{i}', {i})");
        }
        var sw = Stopwatch.StartNew();
        var results = db.ExecuteQuery("SELECT * FROM TestScan");
        sw.Stop();
        Console.WriteLine($"Full table scan on {recordCount} records took {sw.ElapsedMilliseconds:F1} ms");
    }

    [Benchmark]
    public void Insert10kRecords()
    {
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE Test10k (Id INTEGER PRIMARY KEY, Name TEXT, Value INTEGER)");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL($"INSERT INTO Test10k (Name, Value) VALUES ('Test{i}', {i})");
        }
        sw.Stop();
        Console.WriteLine($"Inserting 10k records took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 10000.0:F3} ms per insert");
    }

    [Benchmark]
    public void Select10kRecordsWithQueryCache()
    {
        using var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE Test10k (Id INTEGER PRIMARY KEY, Name TEXT, Value INTEGER)");
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL($"INSERT INTO Test10k (Name, Value) VALUES ('Test{i}', {i})");
        }
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL("SELECT * FROM Test10k WHERE Value < 5000");
        }
        sw.Stop();
        Console.WriteLine($"100 SELECTs on 10k records with cache took {sw.ElapsedMilliseconds:F1} ms → {sw.ElapsedMilliseconds / 100.0:F3} ms per select");
    }
}
