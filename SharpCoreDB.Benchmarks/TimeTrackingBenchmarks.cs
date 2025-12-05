using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Data.SQLite;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing SharpCoreDB with SQLite for time-tracking scenarios.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class TimeTrackingBenchmarks
{
    private IDatabase? _sharpCoreDb;
    private SQLiteConnection? _sqliteConn;
    private readonly string _sharpCoreDbPath = Path.Combine(Path.GetTempPath(), "benchmark_sharpcoredb");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "benchmark_sqlite.db");
    private const int EntryCount = 1000;

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing databases
        if (Directory.Exists(_sharpCoreDbPath))
            Directory.Delete(_sharpCoreDbPath, true);
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);

        // Setup SharpCoreDB
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        _sharpCoreDb = factory.Create(_sharpCoreDbPath, "benchmarkPassword");
        
        _sharpCoreDb.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT)");

        // Setup SQLite
        _sqliteConn = new SQLiteConnection($"Data Source={_sqlitePath};Version=3;");
        _sqliteConn.Open();
        
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = "CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT)";
        cmd.ExecuteNonQuery();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sqliteConn?.Close();
        _sqliteConn?.Dispose();
        
        if (Directory.Exists(_sharpCoreDbPath))
            Directory.Delete(_sharpCoreDbPath, true);
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
    }

    [Benchmark]
    public void SharpCoreDB_Insert()
    {
        for (int i = 0; i < EntryCount; i++)
        {
            _sharpCoreDb!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', '480', 'User{i % 3}')");
        }
    }

    [Benchmark]
    public void SQLite_Insert()
    {
        for (int i = 0; i < EntryCount; i++)
        {
            using var cmd = _sqliteConn!.CreateCommand();
            cmd.CommandText = $"INSERT INTO time_entries VALUES ({i}, 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', 480, 'User{i % 3}')";
            cmd.ExecuteNonQuery();
        }
    }

    [Benchmark]
    public void SharpCoreDB_Select()
    {
        _sharpCoreDb!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
    }

    [Benchmark]
    public void SQLite_Select()
    {
        using var cmd = _sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM time_entries WHERE project = 'Project1'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Consume results
        }
    }
}
