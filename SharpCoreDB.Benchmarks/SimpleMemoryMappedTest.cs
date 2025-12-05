using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Diagnostics;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Simple performance test to demonstrate memory-mapped files improvement.
/// </summary>
public static class SimpleMemoryMappedTest
{
    public static void RunTest()
    {
        Console.WriteLine("=== Memory-Mapped Files Performance Test ===");
        Console.WriteLine();

        const int insertCount = 5_000; // Smaller test for faster execution
        const int selectIterations = 10;

        // Test without memory mapping
        var noMmPath = Path.Combine(Path.GetTempPath(), $"test_nommap_{Guid.NewGuid()}");
        var noMmTime = TestDatabase(noMmPath, false, insertCount, selectIterations);

        // Test with memory mapping
        var mmPath = Path.Combine(Path.GetTempPath(), $"test_mmap_{Guid.NewGuid()}");
        var mmTime = TestDatabase(mmPath, true, insertCount, selectIterations);

        // Display results
        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Without Memory Mapping: {noMmTime:F0} ms");
        Console.WriteLine($"With Memory Mapping:    {mmTime:F0} ms");
        
        var improvement = ((noMmTime - mmTime) / noMmTime) * 100;
        Console.WriteLine($"Performance Improvement: {improvement:F1}%");
        
        // Cleanup
        try
        {
            if (Directory.Exists(noMmPath))
                Directory.Delete(noMmPath, true);
            if (Directory.Exists(mmPath))
                Directory.Delete(mmPath, true);
        }
        catch { }
    }

    private static double TestDatabase(string path, bool useMemoryMapping, int insertCount, int selectIterations)
    {
        Console.WriteLine($"Testing {(useMemoryMapping ? "WITH" : "WITHOUT")} memory mapping...");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        
        var config = new DatabaseConfig 
        { 
            NoEncryptMode = true, 
            UseMemoryMapping = useMemoryMapping,
            MemoryMappingThreshold = 10L * 1024 * 1024 // 10 MB
        };
        
        var db = factory.Create(path, "testPassword", false, config);
        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Insert test data
        Console.WriteLine($"  Inserting {insertCount:N0} records...");
        for (int i = 0; i < insertCount; i++)
        {
            var query = $"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 100}', 'Task{i % 20}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', '480', 'User{i % 10}', 'This is a description for task {i} with some additional text to increase file size.')";
            db.ExecuteSQL(query);
        }

        // Get file size
        var dataFile = Path.Combine(path, "data", "time_entries.bin");
        if (File.Exists(dataFile))
        {
            var fileSize = new FileInfo(dataFile).Length;
            Console.WriteLine($"  Data file size: {fileSize / (1024.0 * 1024.0):F2} MB");
        }

        // Test select performance
        Console.WriteLine($"  Running {selectIterations} select queries...");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < selectIterations; i++)
        {
            db.ExecuteSQL($"SELECT * FROM time_entries WHERE project = 'Project{i % 100}'");
        }
        sw.Stop();

        var totalTime = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  Total time: {totalTime:F0} ms");

        return totalTime;
    }
}
