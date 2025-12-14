// <copyright file="Quick10kComparison.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Quick 10K comparison benchmark - Simple, fast comparison against SQLite and LiteDB.
/// This is the RECOMMENDED benchmark for quick validation.
/// Uses InProcess toolchain to avoid process isolation issues.
/// </summary>
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 3, iterationCount: 10)]
[InProcess]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class Quick10kComparison : IDisposable
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
    
    // Track base ID per iteration to avoid conflicts (same pattern as ComparativeInsertBenchmarks)
    private int currentBaseId = 0;

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine($"\n{'='*70}");
        Console.WriteLine("  QUICK 10K COMPARISON - SharpCoreDB vs SQLite vs LiteDB");
        Console.WriteLine($"{'='*70}\n");
        
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
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
        
        Console.WriteLine("? Setup complete - all databases ready\n");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Increment base ID for next iteration to avoid conflicts
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

    [Benchmark(Description = "SharpCoreDB (No Encryption): 10K Batch Insert")]
    public int SharpCoreDB_NoEncrypt_10K()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var userList = users.Select(u => (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
        
        try
        {
            sharpCoreDbNoEncrypt?.InsertUsersTrueBatch(userList);
            return RecordCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): 10K Batch Insert")]
    public int SharpCoreDB_Encrypted_10K()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var userList = users.Select(u => (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
        
        try
        {
            sharpCoreDbEncrypted?.InsertUsersTrueBatch(userList);
            return RecordCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Baseline = true, Description = "SQLite (Memory): 10K Batch Insert")]
    public int SQLite_Memory_10K()
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

    [Benchmark(Description = "SQLite (File + WAL + FullSync): 10K Batch Insert")]
    public int SQLite_File_WAL_10K()
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

    [Benchmark(Description = "LiteDB: 10K Bulk Insert")]
    public int LiteDB_10K()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        // Use currentBaseId for unique IDs
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
        Dispose();
    }

    public void Dispose()
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

        GC.SuppressFinalize(this);
    }
}
