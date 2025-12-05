using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing SharpCoreDB with and without encryption.
/// Demonstrates the performance benefits of NoEncryption mode.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class NoEncryptionBenchmarks
{
    private IDatabase? _dbEncrypted;
    private IDatabase? _dbNoEncrypt;
    private readonly string _encryptedPath = Path.Combine(Path.GetTempPath(), "benchmark_encrypted");
    private readonly string _noEncryptPath = Path.Combine(Path.GetTempPath(), "benchmark_noencrypt");
    private const int InsertCount = 5000;

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing databases
        if (Directory.Exists(_encryptedPath))
            Directory.Delete(_encryptedPath, true);
        if (Directory.Exists(_noEncryptPath))
            Directory.Delete(_noEncryptPath, true);

        // Setup SharpCoreDB with encryption
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        
        // Create encrypted database
        _dbEncrypted = factory.Create(_encryptedPath, "benchmarkPassword", false, DatabaseConfig.Default);
        _dbEncrypted.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Create non-encrypted database
        _dbNoEncrypt = factory.Create(_noEncryptPath, "benchmarkPassword", false, DatabaseConfig.HighPerformance);
        _dbNoEncrypt.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_encryptedPath))
            Directory.Delete(_encryptedPath, true);
        if (Directory.Exists(_noEncryptPath))
            Directory.Delete(_noEncryptPath, true);
    }

    [Benchmark(Baseline = true)]
    public void SharpCoreDB_Encrypted_Insert()
    {
        for (int i = 0; i < InsertCount; i++)
        {
            _dbEncrypted!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', '480', 'User{i % 3}', 'Description for task {i}')");
        }
    }

    [Benchmark]
    public void SharpCoreDB_NoEncrypt_Insert()
    {
        for (int i = 0; i < InsertCount; i++)
        {
            _dbNoEncrypt!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '2024-01-{(i % 28) + 1:00} 17:00:00', '480', 'User{i % 3}', 'Description for task {i}')");
        }
    }

    [Benchmark]
    public void SharpCoreDB_Encrypted_Select()
    {
        _dbEncrypted!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
    }

    [Benchmark]
    public void SharpCoreDB_NoEncrypt_Select()
    {
        _dbNoEncrypt!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
    }

    [Benchmark]
    public void SharpCoreDB_Encrypted_Update()
    {
        _dbEncrypted!.ExecuteSQL("UPDATE time_entries SET duration = '500' WHERE id = '100'");
    }

    [Benchmark]
    public void SharpCoreDB_NoEncrypt_Update()
    {
        _dbNoEncrypt!.ExecuteSQL("UPDATE time_entries SET duration = '500' WHERE id = '100'");
    }
}
