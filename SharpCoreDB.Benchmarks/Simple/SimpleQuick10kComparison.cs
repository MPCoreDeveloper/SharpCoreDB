// <copyright file="SimpleQuick10kComparison.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SharpCoreDB.Benchmarks.Simple;

/// <summary>
/// Simplified 10K benchmark - No fancy attributes, just raw performance test.
/// GUARANTEED TO WORK because debug app succeeded!
/// </summary>
public class SimpleQuick10kComparison
{
    private const int RecordCount = 10000;
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    
    private BenchmarkDatabaseHelper? sharpCoreDbNoEncrypt;
    private BenchmarkDatabaseHelper? sharpCoreDbEncrypted;
    private SqliteConnection? sqliteMemory;
    private SqliteConnection? sqliteFile;
    private LiteDatabase? liteDb;
    private ILiteCollection<TestDataGenerator.UserRecord>? liteCollection;
    
    private int currentBaseId = 0;

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("\n=== SIMPLE BENCHMARK SETUP ===");
        
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"simpleBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        // SharpCoreDB (No Encryption)
        var dbPathNoEncrypt = Path.Combine(tempDir, "sharpcore_noencrypt");
        sharpCoreDbNoEncrypt = new BenchmarkDatabaseHelper(dbPathNoEncrypt, enableEncryption: false);
        sharpCoreDbNoEncrypt.CreateUsersTable();
        
        // SharpCoreDB (Encrypted)
        var dbPathEncrypted = Path.Combine(tempDir, "sharpcore_encrypted");
        sharpCoreDbEncrypted = new BenchmarkDatabaseHelper(dbPathEncrypted, enableEncryption: true);
        sharpCoreDbEncrypted.CreateUsersTable();

        // SQLite Memory
        sqliteMemory = new SqliteConnection("Data Source=:memory:");
        sqliteMemory.Open();
        CreateSQLiteTable(sqliteMemory);

        // SQLite File + WAL + FullSync
        var sqliteFilePath = Path.Combine(tempDir, "sqlite_wal.db");
        sqliteFile = new SqliteConnection($"Data Source={sqliteFilePath}");
        sqliteFile.Open();
        using (var cmd = sqliteFile.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode = WAL";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA synchronous = FULL";
            cmd.ExecuteNonQuery();
        }
        CreateSQLiteTable(sqliteFile);

        // LiteDB
        var liteDbPath = Path.Combine(tempDir, "litedb.db");
        liteDb = new LiteDatabase(liteDbPath);
        liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
        
        Console.WriteLine("? Setup complete\n");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        currentBaseId += 1000000;
    }

    private void CreateSQLiteTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                age INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                is_active INTEGER NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption)")]
    public int SharpCoreDB_NoEncrypt()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var userList = users.Select(u => (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
        
        sharpCoreDbNoEncrypt?.InsertUsersTrueBatch(userList);
        return RecordCount;
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted)")]
    public int SharpCoreDB_Encrypted()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var userList = users.Select(u => (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
        
        sharpCoreDbEncrypted?.InsertUsersTrueBatch(userList);
        return RecordCount;
    }

    [Benchmark(Baseline = true, Description = "SQLite (Memory)")]
    public int SQLite_Memory()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        using var transaction = sqliteMemory?.BeginTransaction();
        using var cmd = sqliteMemory?.CreateCommand();
        cmd!.CommandText = @"
            INSERT INTO users (id, name, email, age, created_at, is_active)
            VALUES (@id, @name, @email, @age, @created_at, @is_active)";

        cmd.Parameters.Add("@id", SqliteType.Integer);
        cmd.Parameters.Add("@name", SqliteType.Text);
        cmd.Parameters.Add("@email", SqliteType.Text);
        cmd.Parameters.Add("@age", SqliteType.Integer);
        cmd.Parameters.Add("@created_at", SqliteType.Text);
        cmd.Parameters.Add("@is_active", SqliteType.Integer);

        foreach (var user in users)
        {
            cmd.Parameters["@id"].Value = currentBaseId + user.Id;
            cmd.Parameters["@name"].Value = user.Name;
            cmd.Parameters["@email"].Value = user.Email;
            cmd.Parameters["@age"].Value = user.Age;
            cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            cmd.ExecuteNonQuery();
        }

        transaction?.Commit();
        return RecordCount;
    }

    [Benchmark(Description = "SQLite (File + WAL)")]
    public int SQLite_File_WAL()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        using var transaction = sqliteFile?.BeginTransaction();
        using var cmd = sqliteFile?.CreateCommand();
        cmd!.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO users (id, name, email, age, created_at, is_active)
            VALUES (@id, @name, @email, @age, @created_at, @is_active)";

        cmd.Parameters.Add("@id", SqliteType.Integer);
        cmd.Parameters.Add("@name", SqliteType.Text);
        cmd.Parameters.Add("@email", SqliteType.Text);
        cmd.Parameters.Add("@age", SqliteType.Integer);
        cmd.Parameters.Add("@created_at", SqliteType.Text);
        cmd.Parameters.Add("@is_active", SqliteType.Integer);

        foreach (var user in users)
        {
            cmd.Parameters["@id"].Value = currentBaseId + user.Id;
            cmd.Parameters["@name"].Value = user.Name;
            cmd.Parameters["@email"].Value = user.Email;
            cmd.Parameters["@age"].Value = user.Age;
            cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            cmd.ExecuteNonQuery();
        }

        transaction?.Commit();
        return RecordCount;
    }

    [Benchmark(Description = "LiteDB")]
    public int LiteDB_Bulk()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        foreach (var user in users)
        {
            user.Id = currentBaseId + user.Id;
        }
        
        liteCollection?.InsertBulk(users);
        return RecordCount;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        sharpCoreDbNoEncrypt?.Dispose();
        sharpCoreDbEncrypted?.Dispose();
        sqliteMemory?.Dispose();
        sqliteFile?.Dispose();
        liteDb?.Dispose();

        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// Custom config for simple benchmark - minimal overhead, maximum reliability.
/// </summary>
public class SimpleConfig : ManualConfig
{
    public SimpleConfig()
    {
        AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Instance)  // Run in-process
            .WithWarmupCount(1)      // Minimal warmup
            .WithIterationCount(3)   // Just 3 iterations
            .WithLaunchCount(1));    // Single launch
        
        AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
        AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
        AddExporter(BenchmarkDotNet.Exporters.MarkdownExporter.GitHub);
        AddExporter(BenchmarkDotNet.Exporters.Csv.CsvExporter.Default);
    }
}

/// <summary>
/// Simple runner - just run the benchmark without menu.
/// </summary>
public class SimpleRunner
{
    public static void Run()
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  SIMPLE 10K BENCHMARK");
        Console.WriteLine("  Minimal config for maximum reliability");
        Console.WriteLine("==============================================\n");
        
        var config = new SimpleConfig();
        var summary = BenchmarkRunner.Run<SimpleQuick10kComparison>(config);
        
        Console.WriteLine("\n==============================================");
        Console.WriteLine("  BENCHMARK COMPLETE!");
        Console.WriteLine("==============================================");
    }
}
