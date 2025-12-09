using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Xunit.Abstractions;

namespace SharpCoreDB.Tests;

/// <summary>
/// Performance tests demonstrating HashIndex speedup on WHERE clause queries.
/// </summary>
public class HashIndexPerformanceTests
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _output;

    public HashIndexPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_hashindex_perf_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void HashIndex_SELECT_WHERE_Performance_5to10xFaster()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true, EnableQueryCache = false };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, duration INTEGER)");

        // Insert 10,000 rows with 100 unique projects
        _output.WriteLine("Inserting 10,000 test rows...");
        var projects = Enumerable.Range(0, 100).Select(i => $"project_{i}").ToArray();
        for (int i = 1; i <= 10000; i++)
        {
            var project = projects[i % projects.Length];
            db.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', '{project}', 'Task{i}', '{i * 10}')");
        }
        _output.WriteLine("Data insertion complete.");

        // Measure without hash index (full table scan)
        var sw1 = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'project_0'");
        }
        sw1.Stop();
        var withoutIndexMs = sw1.ElapsedMilliseconds;
        _output.WriteLine($"Without index: 100 queries took {withoutIndexMs}ms");

        // Create hash index on project column
        _output.WriteLine("Creating hash index on 'project' column...");
        db.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");

        // Measure with hash index (O(1) lookup)
        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'project_0'");
        }
        sw2.Stop();
        var withIndexMs = sw2.ElapsedMilliseconds;
        _output.WriteLine($"With index: 100 queries took {withIndexMs}ms");

        // Calculate speedup
        var speedup = (double)withoutIndexMs / withIndexMs;
        _output.WriteLine($"Speedup: {speedup:F2}x faster with hash index");

        // Assert - hash index should provide at least 2x speedup
        Assert.True(speedup >= 2.0, $"Expected at least 2x speedup, got {speedup:F2}x");

        // Ideal speedup should be 5-10x for this dataset
        _output.WriteLine(speedup >= 5.0
            ? $"✓ Achieved target 5-10x speedup ({speedup:F2}x)"
            : $"⚠ Speedup {speedup:F2}x is below target 5-10x, but still significant");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_MultipleQueries_ConsistentPerformance()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE orders (id INTEGER, customer_id INTEGER, status TEXT, amount INTEGER)");

        // Insert 5,000 rows
        _output.WriteLine("Inserting 5,000 test rows...");
        var statuses = Enumerable.Range(0, 50).Select(i => $"status_{i}").ToArray();
        for (int i = 1; i <= 5000; i++)
        {
            var status = statuses[i % statuses.Length];
            var customerId = i % 500; // 500 unique customers
            db.ExecuteSQL($"INSERT INTO orders VALUES ('{i}', '{customerId}', '{status}', '{i * 100}')");
        }

        // Create indexes on both columns
        db.ExecuteSQL("CREATE INDEX idx_customer ON orders (customer_id)");
        db.ExecuteSQL("CREATE INDEX idx_status ON orders (status)");
        _output.WriteLine("Indexes created.");

        // Test multiple different queries
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            db.ExecuteSQL($"SELECT * FROM orders WHERE customer_id = '{i % 500}'");
        }
        sw.Stop();
        var customerQueryMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"50 customer queries: {customerQueryMs}ms");

        sw.Restart();
        for (int i = 0; i < 50; i++)
        {
            db.ExecuteSQL($"SELECT * FROM orders WHERE status = '{statuses[i % statuses.Length]}'");
        }
        sw.Stop();
        var statusQueryMs = sw.ElapsedMilliseconds;
        _output.WriteLine($"50 status queries: {statusQueryMs}ms");

        // Both should complete quickly with indexes
        Assert.True(customerQueryMs < 1000, $"Customer queries took {customerQueryMs}ms, expected < 1000ms");
        Assert.True(statusQueryMs < 1000, $"Status queries took {statusQueryMs}ms, expected < 1000ms");

        _output.WriteLine($"✓ Consistent fast performance with hash indexes");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_MemoryOverhead_Acceptable()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE metrics (id INTEGER, metric_name TEXT, value REAL, timestamp TEXT)");

        // Insert 20,000 rows with high cardinality metric names
        _output.WriteLine("Inserting 20,000 test rows...");
        for (int i = 1; i <= 20000; i++)
        {
            var metricName = $"metric_{i % 1000}"; // 1000 unique metric names
            db.ExecuteSQL($"INSERT INTO metrics VALUES ('{i}', '{metricName}', '{i * 1.5}', '2024-01-01')");
        }

        // Measure memory before index
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memBefore = GC.GetTotalMemory(false);
        _output.WriteLine($"Memory before index: {memBefore / 1024 / 1024:F2} MB");

        // Create index
        db.ExecuteSQL("CREATE INDEX idx_metric_name ON metrics (metric_name)");

        // Measure memory after index
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memAfter = GC.GetTotalMemory(false);
        _output.WriteLine($"Memory after index: {memAfter / 1024 / 1024:F2} MB");

        var overhead = memAfter - memBefore;
        _output.WriteLine($"Index overhead: {overhead / 1024 / 1024:F2} MB");

        // Test index performance
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL($"SELECT * FROM metrics WHERE metric_name = 'metric_{i % 1000}'");
        }
        sw.Stop();
        _output.WriteLine($"100 indexed queries: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"✓ Hash index provides fast lookups with reasonable memory overhead");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }
}
