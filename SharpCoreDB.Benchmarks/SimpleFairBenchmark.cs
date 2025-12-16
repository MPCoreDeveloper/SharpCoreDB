// Simple Fair Comparison Benchmark - SharpCoreDB vs SQLite vs LiteDB
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using LiteDB;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

public class SimpleFairBenchmark
{
    private const int RECORDS = 10_000;
    private readonly string tempDir;
    private readonly TestDataGenerator dataGen;

    public SimpleFairBenchmark()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"benchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        dataGen = new TestDataGenerator();
    }

    public void Run()
    {
        Console.WriteLine("=== FAIR BENCHMARK: 10K INSERTS ===\n");

        // SQLite
        var sqliteTime = RunSQLite();
        Console.WriteLine($"SQLite:     {sqliteTime:N0} ms\n");

        // LiteDB
        var liteTime = RunLiteDB();
        Console.WriteLine($"LiteDB:     {liteTime:N0} ms\n");

        // SharpCoreDB (simple versie - geen fancy config)
        var sharpTime = RunSharpCore();
        Console.WriteLine($"SharpCore:  {sharpTime:N0} ms\n");

        // Winner
        var min = Math.Min(sqliteTime, Math.Min(liteTime, sharpTime));
        var winner = min == sqliteTime ? "SQLite" : min == liteTime ? "LiteDB" : "SharpCore";
        Console.WriteLine($"?? Winner: {winner}");

        // Cleanup
        try { Directory.Delete(tempDir, true); } catch { }
    }

    private long RunSQLite()
    {
        var path = Path.Combine(tempDir, "sqlite.db");
        
        // Delete any existing database files
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA page_size=4096;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, email TEXT, age INT, created_at TEXT, is_active INT)";
            cmd.ExecuteNonQuery();
        }

        var users = dataGen.GenerateUsers(RECORDS);
        var sw = Stopwatch.StartNew();

        using var txn = conn.BeginTransaction();
        using var insert = conn.CreateCommand();
        insert.Transaction = txn;
        insert.CommandText = "INSERT INTO users VALUES (@id, @name, @email, @age, @created_at, @is_active)";
        insert.Parameters.Add("@id", SqliteType.Integer);
        insert.Parameters.Add("@name", SqliteType.Text);
        insert.Parameters.Add("@email", SqliteType.Text);
        insert.Parameters.Add("@age", SqliteType.Integer);
        insert.Parameters.Add("@created_at", SqliteType.Text);
        insert.Parameters.Add("@is_active", SqliteType.Integer);

        for (int i = 0; i < users.Count; i++)
        {
            var u = users[i];
            insert.Parameters["@id"].Value = i;
            insert.Parameters["@name"].Value = u.Name;
            insert.Parameters["@email"].Value = u.Email;
            insert.Parameters["@age"].Value = u.Age;
            insert.Parameters["@created_at"].Value = u.CreatedAt.ToString("o");
            insert.Parameters["@is_active"].Value = u.IsActive ? 1 : 0;
            insert.ExecuteNonQuery();
        }

        txn.Commit();
        return sw.ElapsedMilliseconds;
    }

    private long RunLiteDB()
    {
        var path = Path.Combine(tempDir, "litedb.db");
        
        // Delete any existing database file
        if (File.Exists(path))
            File.Delete(path);
        
        using var db = new LiteDatabase(path);
        var col = db.GetCollection<TestDataGenerator.UserRecord>("users");

        var users = dataGen.GenerateUsers(RECORDS);
        
        // Generate unique IDs to avoid duplicate key errors
        int baseId = Random.Shared.Next(1000000, 9000000);
        for (int i = 0; i < users.Count; i++)
            users[i].Id = baseId + i;

        var sw = Stopwatch.StartNew();
        col.InsertBulk(users);
        return sw.ElapsedMilliseconds;
    }

    private long RunSharpCore()
    {
        var path = Path.Combine(tempDir, "sharpcore");
        
        // Delete any existing database directory/files
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        var dbFile = path + ".db";
        var walFile = path + ".wal";
        if (File.Exists(dbFile)) File.Delete(dbFile);
        if (File.Exists(walFile)) File.Delete(walFile);
        
        // SIMPEL: gebruik de WERKENDE constructor
        using var helper = new BenchmarkDatabaseHelper(path, "pwd", enableEncryption: false);
        helper.CreateUsersTable();

        var users = dataGen.GenerateUsers(RECORDS);
        var list = users.Select((u, i) => (i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();

        var sw = Stopwatch.StartNew();
        helper.InsertUsersTrueBatch(list);
        return sw.ElapsedMilliseconds;
    }
}
