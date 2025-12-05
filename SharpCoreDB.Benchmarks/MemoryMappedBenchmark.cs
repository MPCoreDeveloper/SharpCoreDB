using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing SharpCoreDB with and without memory-mapped files.
/// Demonstrates the performance benefits of memory mapping for large file reads.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class MemoryMappedBenchmark
{
    private IDatabase? _dbWithMemoryMapping;
    private IDatabase? _dbWithoutMemoryMapping;
    private readonly string _mmPath = Path.Combine(Path.GetTempPath(), "benchmark_mmap");
    private readonly string _noMmPath = Path.Combine(Path.GetTempPath(), "benchmark_nommap");
    private const int InsertCount = 50_000; // Large dataset to ensure files exceed memory mapping threshold

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing databases
        if (Directory.Exists(_mmPath))
            Directory.Delete(_mmPath, true);
        if (Directory.Exists(_noMmPath))
            Directory.Delete(_noMmPath, true);

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        
        // Create database with memory mapping enabled
        var mmConfig = new DatabaseConfig 
        { 
            NoEncryptMode = true, 
            UseMemoryMapping = true,
            MemoryMappingThreshold = 10L * 1024 * 1024 // 10 MB
        };
        _dbWithMemoryMapping = factory.Create(_mmPath, "benchmarkPassword", false, mmConfig);
        _dbWithMemoryMapping.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Create database without memory mapping
        var noMmConfig = new DatabaseConfig 
        { 
            NoEncryptMode = true, 
            UseMemoryMapping = false
        };
        _dbWithoutMemoryMapping = factory.Create(_noMmPath, "benchmarkPassword", false, noMmConfig);
        _dbWithoutMemoryMapping.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Insert data to create large files that exceed memory mapping threshold
        Console.WriteLine("Inserting test data...");
        for (int i = 0; i < InsertCount; i++)
        {
            var query = $"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 100}', 'Task{i % 20}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', '480', 'User{i % 10}', 'This is a longer description for task {i} to increase file size and exceed memory mapping threshold.')";
            _dbWithMemoryMapping!.ExecuteSQL(query);
            _dbWithoutMemoryMapping!.ExecuteSQL(query);
        }
        Console.WriteLine("Test data inserted.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_mmPath))
            Directory.Delete(_mmPath, true);
        if (Directory.Exists(_noMmPath))
            Directory.Delete(_noMmPath, true);
    }

    [Benchmark(Baseline = true)]
    public void Select_WithoutMemoryMapping()
    {
        // Select a subset of records
        _dbWithoutMemoryMapping!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project50'");
    }

    [Benchmark]
    public void Select_WithMemoryMapping()
    {
        // Select a subset of records with memory mapping enabled
        _dbWithMemoryMapping!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project50'");
    }

    [Benchmark]
    public void SelectMultiple_WithoutMemoryMapping()
    {
        // Select multiple large result sets
        for (int i = 0; i < 10; i++)
        {
            _dbWithoutMemoryMapping!.ExecuteSQL($"SELECT * FROM time_entries WHERE user = 'User{i % 10}'");
        }
    }

    [Benchmark]
    public void SelectMultiple_WithMemoryMapping()
    {
        // Select multiple large result sets with memory mapping
        for (int i = 0; i < 10; i++)
        {
            _dbWithMemoryMapping!.ExecuteSQL($"SELECT * FROM time_entries WHERE user = 'User{i % 10}'");
        }
    }

    [Benchmark]
    public void SelectAll_WithoutMemoryMapping()
    {
        // Full table scan without memory mapping
        _dbWithoutMemoryMapping!.ExecuteSQL("SELECT * FROM time_entries");
    }

    [Benchmark]
    public void SelectAll_WithMemoryMapping()
    {
        // Full table scan with memory mapping
        _dbWithMemoryMapping!.ExecuteSQL("SELECT * FROM time_entries");
    }
}
