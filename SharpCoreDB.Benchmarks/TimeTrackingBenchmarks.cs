using BenchmarkDotNet.Attributes;
using Bogus;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Data.SQLite;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing SharpCoreDB with SQLite and LiteDB for time-tracking scenarios.
/// Scaled to 100k inserts with GC/allocation metrics and Native AOT support.
/// </summary>
[MemoryDiagnoser]
[GcServer(true)]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class TimeTrackingBenchmarks
{
    private IDatabase? _sharpCoreDb;
    private SQLiteConnection? _sqliteConn;
    private LiteDatabase? _liteDb;
    private ILiteCollection<TimeEntry>? _liteCollection;
    private readonly string _sharpCoreDbPath = Path.Combine(Path.GetTempPath(), "benchmark_sharpcoredb");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "benchmark_sqlite.db");
    private readonly string _liteDbPath = Path.Combine(Path.GetTempPath(), "benchmark_litedb.db");
    private const int EntryCount = 100000;
    private List<TimeEntry>? _testData;

    /// <summary>
    /// TimeEntry model for LiteDB
    /// </summary>
    private class TimeEntry
    {
        public int Id { get; set; }
        public string Project { get; set; } = "";
        public string Task { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Duration { get; set; }
        public string User { get; set; } = "";
    }

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing databases
        if (Directory.Exists(_sharpCoreDbPath))
            Directory.Delete(_sharpCoreDbPath, true);
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
        if (File.Exists(_liteDbPath))
            File.Delete(_liteDbPath);

        // Generate realistic fake data using Bogus
        var faker = new Faker<TimeEntry>()
            .RuleFor(e => e.Id, f => f.IndexFaker)
            .RuleFor(e => e.Project, f => f.PickRandom(new[] { "Alpha Project", "Beta Development", "Gamma Infrastructure", "Delta Research", "Epsilon Marketing", "Zeta Support", "Eta Analytics", "Theta Operations", "Iota Sales", "Kappa Training" }))
            .RuleFor(e => e.Task, f => f.PickRandom(new[] { "Code Review", "Feature Development", "Bug Fixing", "Testing", "Documentation", "Deployment", "Meeting", "Planning", "Research", "Support" }))
            .RuleFor(e => e.User, f => f.Name.FullName())
            .RuleFor(e => e.StartTime, f => f.Date.Between(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)))
            .RuleFor(e => e.Duration, f => f.Random.Int(15, 480))
            .RuleFor(e => e.EndTime, (f, e) => e.StartTime.AddMinutes(e.Duration));

        _testData = faker.Generate(EntryCount);

        // Setup SharpCoreDB with HighPerformance config (NoEncryption mode)
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();
        _sharpCoreDb = factory.Create(_sharpCoreDbPath, "benchmarkPassword", false, DatabaseConfig.HighPerformance);
        
        _sharpCoreDb.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT)");

        // Setup SQLite
        _sqliteConn = new SQLiteConnection($"Data Source={_sqlitePath};Version=3;");
        _sqliteConn.Open();
        
        using var cmd = _sqliteConn.CreateCommand();
        cmd.CommandText = "CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, end_time DATETIME, duration INTEGER, user TEXT)";
        cmd.ExecuteNonQuery();

        // Setup LiteDB
        _liteDb = new LiteDatabase(_liteDbPath);
        _liteCollection = _liteDb.GetCollection<TimeEntry>("time_entries");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sqliteConn?.Close();
        _sqliteConn?.Dispose();
        _liteDb?.Dispose();
        
        if (Directory.Exists(_sharpCoreDbPath))
            Directory.Delete(_sharpCoreDbPath, true);
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
        if (File.Exists(_liteDbPath))
            File.Delete(_liteDbPath);
    }

    [Benchmark]
    public void SharpCoreDB_Insert()
    {
        // IMPORTANT: This is benchmark code for performance testing only.
        // In production, always use parameterized queries or proper sanitization.
        // We escape single quotes here to prevent SQL injection with controlled test data.
        // This benchmark uses pre-generated fake data for realistic testing.
        for (int i = 0; i < _testData.Count; i++)
        {
            var entry = _testData[i];
            var project = entry.Project.Replace("'", "''");
            var task = entry.Task.Replace("'", "''");
            var user = entry.User.Replace("'", "''");
            var startTime = entry.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            var endTime = entry.EndTime.ToString("yyyy-MM-dd HH:mm:ss");
            _sharpCoreDb!.ExecuteSQL($"INSERT INTO time_entries VALUES ('{entry.Id}', '{project}', '{task}', '{startTime}', '{endTime}', '{entry.Duration}', '{user}')");

            // Progress indicator
            if ((i + 1) % 10000 == 0)
            {
                Console.WriteLine($"SharpCoreDB Insert Progress: {i + 1}/{_testData.Count}");
            }
        }
    }

    [Benchmark]
    public void SQLite_Insert()
    {
        // IMPORTANT: This is benchmark code for performance testing only.
        // In production, always use SQLiteParameter for parameterized queries.
        // The test data is controlled and pre-validated, no user input is used.
        for (int i = 0; i < _testData.Count; i++)
        {
            var entry = _testData[i];
            using var cmd = _sqliteConn!.CreateCommand();
            var project = entry.Project.Replace("'", "''");
            var task = entry.Task.Replace("'", "''");
            var user = entry.User.Replace("'", "''");
            var startTime = entry.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            var endTime = entry.EndTime.ToString("yyyy-MM-dd HH:mm:ss");
            cmd.CommandText = $"INSERT INTO time_entries VALUES ({entry.Id}, '{project}', '{task}', '{startTime}', '{endTime}', {entry.Duration}, '{user}')";
            cmd.ExecuteNonQuery();

            // Progress indicator
            if ((i + 1) % 10000 == 0)
            {
                Console.WriteLine($"SQLite Insert Progress: {i + 1}/{_testData.Count}");
            }
        }
    }

    [Benchmark]
    public void LiteDB_Insert()
    {
        Console.WriteLine("Starting LiteDB bulk insert...");
        _liteCollection!.InsertBulk(_testData!);
        Console.WriteLine("LiteDB bulk insert completed.");
    }

    [Benchmark]
    public void SharpCoreDB_Select()
    {
        _sharpCoreDb!.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Alpha Project'");
    }

    [Benchmark]
    public void SQLite_Select()
    {
        using var cmd = _sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM time_entries WHERE project = 'Alpha Project'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Consume results
        }
    }

    [Benchmark]
    public void LiteDB_Select()
    {
        var results = _liteCollection!.Find(e => e.Project == "Alpha Project").ToList();
    }

    [Benchmark]
    public void SharpCoreDB_GroupBy()
    {
        _sharpCoreDb!.ExecuteSQL("SELECT project, SUM(duration) FROM time_entries GROUP BY project");
    }

    [Benchmark]
    public void SQLite_GroupBy()
    {
        using var cmd = _sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT project, SUM(duration) FROM time_entries GROUP BY project";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Consume results
        }
    }

    [Benchmark]
    public void LiteDB_GroupBy()
    {
        // LiteDB doesn't have native GROUP BY, so we do it in-memory
        var allEntries = _liteCollection!.FindAll().ToList();
        var results = allEntries
            .GroupBy(e => e.Project)
            .Select(g => new { Project = g.Key, TotalDuration = g.Sum(e => e.Duration) })
            .ToList();
    }
}
