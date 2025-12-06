using BenchmarkDotNet.Attributes;
using Bogus;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Data.SQLite;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Comprehensive benchmarks comparing SharpCoreDB with SQLite and LiteDB
/// using realistic time-tracking data generated with Bogus.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class ComprehensiveBenchmark
{
    private class TimeEntry
    {
        public int Id { get; set; }
        public string Project { get; set; } = string.Empty;
        public string Task { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Duration { get; set; }
        public string User { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private IDatabase? _sharpCoreDbEncrypted;
    private IDatabase? _sharpCoreDbNoEncrypt;
    private SQLiteConnection? _sqliteConn;
    private LiteDatabase? _liteDb;
    private List<TimeEntry> _testData = new();
    
    private readonly string _sharpCoreEncryptedPath = Path.Combine(Path.GetTempPath(), "bench_sharpcoredb_encrypted");
    private readonly string _sharpCoreNoEncryptPath = Path.Combine(Path.GetTempPath(), "bench_sharpcoredb_noencrypt");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "bench_sqlite.db");
    private readonly string _liteDbPath = Path.Combine(Path.GetTempPath(), "bench_litedb.db");
    
    private const int DataCount = 100000;

    [GlobalSetup]
    public void Setup()
    {
        // Clean up existing databases
        CleanupDatabases();

        // Generate realistic time-tracking data with Bogus
        var faker = new Faker<TimeEntry>()
            .RuleFor(t => t.Id, f => f.IndexFaker)
            .RuleFor(t => t.Project, f => f.Commerce.Department())
            .RuleFor(t => t.Task, f => f.Hacker.Verb() + " " + f.Hacker.Noun())
            .RuleFor(t => t.StartTime, f => f.Date.Between(DateTime.Now.AddMonths(-3), DateTime.Now))
            .RuleFor(t => t.Duration, f => f.Random.Int(15, 480))
            .RuleFor(t => t.User, f => f.Internet.UserName())
            .RuleFor(t => t.Description, f => f.Lorem.Sentence());

        _testData = faker.Generate(DataCount);
        
        // Calculate end times
        foreach (var entry in _testData)
        {
            entry.EndTime = entry.StartTime.AddMinutes(entry.Duration);
        }

        // Setup SharpCoreDB with encryption
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        
        _sharpCoreDbEncrypted = factory.Create(_sharpCoreEncryptedPath, "benchmarkPassword", false, DatabaseConfig.Default);
        _sharpCoreDbEncrypted.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Setup SharpCoreDB without encryption
        _sharpCoreDbNoEncrypt = factory.Create(_sharpCoreNoEncryptPath, "benchmarkPassword", false, DatabaseConfig.HighPerformance);
        _sharpCoreDbNoEncrypt.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)");

        // Setup SQLite
        _sqliteConn = new SQLiteConnection($"Data Source={_sqlitePath};Version=3;");
        _sqliteConn.Open();
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = "CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT, description TEXT)";
        cmd.ExecuteNonQuery();

        // Setup LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        var col = _liteDb.GetCollection<TimeEntry>("time_entries");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sqliteConn?.Close();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        CleanupDatabases();
    }

    private void CleanupDatabases()
    {
        if (Directory.Exists(_sharpCoreEncryptedPath))
            Directory.Delete(_sharpCoreEncryptedPath, true);
        if (Directory.Exists(_sharpCoreNoEncryptPath))
            Directory.Delete(_sharpCoreNoEncryptPath, true);
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
        if (File.Exists(_liteDbPath))
            File.Delete(_liteDbPath);
    }

    [Benchmark]
    public void SharpCoreDB_Encrypted_Insert()
    {
        foreach (var entry in _testData)
        {
            _sharpCoreDbEncrypted!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{entry.Id}', '{entry.Project.Replace("'", "''")}', '{entry.Task.Replace("'", "''")}', '{entry.StartTime:yyyy-MM-dd HH:mm:ss}', '{entry.EndTime:yyyy-MM-dd HH:mm:ss}', '{entry.Duration}', '{entry.User.Replace("'", "''")}', '{entry.Description.Replace("'", "''")}')");
        }
    }

    [Benchmark(Baseline = true)]
    public void SharpCoreDB_NoEncrypt_Insert()
    {
        foreach (var entry in _testData)
        {
            _sharpCoreDbNoEncrypt!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{entry.Id}', '{entry.Project.Replace("'", "''")}', '{entry.Task.Replace("'", "''")}', '{entry.StartTime:yyyy-MM-dd HH:mm:ss}', '{entry.EndTime:yyyy-MM-dd HH:mm:ss}', '{entry.Duration}', '{entry.User.Replace("'", "''")}', '{entry.Description.Replace("'", "''")}')");
        }
    }

    [Benchmark]
    public void SQLite_Insert()
    {
        foreach (var entry in _testData)
        {
            using var cmd = _sqliteConn!.CreateCommand();
            cmd.CommandText = $"INSERT INTO time_entries VALUES ({entry.Id}, '{entry.Project.Replace("'", "''")}', '{entry.Task.Replace("'", "''")}', '{entry.StartTime:yyyy-MM-dd HH:mm:ss}', '{entry.EndTime:yyyy-MM-dd HH:mm:ss}', {entry.Duration}, '{entry.User.Replace("'", "''")}', '{entry.Description.Replace("'", "''")}')";
            cmd.ExecuteNonQuery();
        }
    }

    [Benchmark]
    public void LiteDB_Insert()
    {
        var col = _liteDb!.GetCollection<TimeEntry>("time_entries");
        foreach (var entry in _testData)
        {
            col.Insert(entry);
        }
    }

    [Benchmark]
    public void SharpCoreDB_NoEncrypt_Select()
    {
        _sharpCoreDbNoEncrypt!.ExecuteSQL("SELECT * FROM time_entries WHERE duration = '60'");
    }

    [Benchmark]
    public void SQLite_Select()
    {
        using var cmd = _sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM time_entries WHERE duration = 60";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Consume results
        }
    }

    [Benchmark]
    public void LiteDB_Select()
    {
        var col = _liteDb!.GetCollection<TimeEntry>("time_entries");
        var results = col.Find(x => x.Duration == 60).ToList();
    }

    [Benchmark]
    public void SharpCoreDB_NoEncrypt_BatchInsert()
    {
        var batch = _testData.Select(entry => 
            $"INSERT INTO time_entries VALUES ('{entry.Id}', '{entry.Project.Replace("'", "''")}', '{entry.Task.Replace("'", "''")}', '{entry.StartTime:yyyy-MM-dd HH:mm:ss}', '{entry.EndTime:yyyy-MM-dd HH:mm:ss}', '{entry.Duration}', '{entry.User.Replace("'", "''")}', '{entry.Description.Replace("'", "''")}')");
        _sharpCoreDbNoEncrypt!.ExecuteBatchSQL(batch);
    }

    [Benchmark]
    public void SQLite_BatchInsert()
    {
        using var transaction = _sqliteConn!.BeginTransaction();
        foreach (var entry in _testData)
        {
            using var cmd = _sqliteConn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"INSERT INTO time_entries VALUES ({entry.Id}, '{entry.Project.Replace("'", "''")}', '{entry.Task.Replace("'", "''")}', '{entry.StartTime:yyyy-MM-dd HH:mm:ss}', '{entry.EndTime:yyyy-MM-dd HH:mm:ss}', {entry.Duration}, '{entry.User.Replace("'", "''")}', '{entry.Description.Replace("'", "''")}')";
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    [Benchmark]
    public void LiteDB_BatchInsert()
    {
        var col = _liteDb!.GetCollection<TimeEntry>("time_entries");
        col.InsertBulk(_testData);
    }
}
