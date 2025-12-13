// <copyright file="GroupCommitWALBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;
using SharpCoreDB.Services;
using System.Text;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Comprehensive benchmarks comparing SharpCoreDB's new GroupCommitWAL against SQLite and LiteDB.
/// Tests various write-heavy scenarios that benefit from group commits.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class GroupCommitWALBenchmarks : IDisposable
{
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    
    // SharpCoreDB variants
    private BenchmarkDatabaseHelper? sharpCoreDb_LegacyWAL;
    private BenchmarkDatabaseHelper? sharpCoreDb_GroupCommitFullSync;
    private BenchmarkDatabaseHelper? sharpCoreDb_GroupCommitAsync;
    
    // SQLite variants
    private SqliteConnection? sqliteMemory;
    private SqliteConnection? sqliteFile_WAL;
    private SqliteConnection? sqliteFile_NoWAL;
    private string sqliteFileWalPath = null!;
    private string sqliteFileNoWalPath = null!;
    
    // LiteDB
    private LiteDatabase? liteDb;
    private ILiteCollection<TestDataGenerator.UserRecord>? liteCollection;
    private string liteDbFilePath = null!;
    
    private int currentBaseId = 0;

    [Params(10, 100, 1000)]
    public int RecordCount { get; set; }

    [Params(1, 4, 8, 16)]  // ? ADDED: 8 threads (sweet spot!)
    public int ConcurrentThreads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"groupCommitBench_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        SetupSharpCoreDB();
        SetupSQLite();
        SetupLiteDB();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        currentBaseId += 10000000;
    }

    private void SetupSharpCoreDB()
    {
        try
        {
            // Legacy WAL (for comparison)
            var legacyPath = Path.Combine(tempDir, "sharpcore_legacy");
            var legacyConfig = new DatabaseConfig
            {
                NoEncryptMode = true,
                UseGroupCommitWal = false,
                EnablePageCache = true,
                PageCacheCapacity = 1000,
            };
            sharpCoreDb_LegacyWAL = new BenchmarkDatabaseHelper(legacyPath, config: legacyConfig);
            sharpCoreDb_LegacyWAL.CreateUsersTable();
            Console.WriteLine("? SharpCoreDB (Legacy WAL) setup complete");

            // Group Commit - FullSync
            var groupCommitFullSyncPath = Path.Combine(tempDir, "sharpcore_groupcommit_fullsync");
            var fullSyncConfig = new DatabaseConfig
            {
                NoEncryptMode = true,
                UseGroupCommitWal = true,
                WalDurabilityMode = DurabilityMode.FullSync,
                WalMaxBatchSize = 100,
                WalMaxBatchDelayMs = 10,
                EnablePageCache = true,
                PageCacheCapacity = 1000,
            };
            sharpCoreDb_GroupCommitFullSync = new BenchmarkDatabaseHelper(groupCommitFullSyncPath, config: fullSyncConfig);
            sharpCoreDb_GroupCommitFullSync.CreateUsersTable();
            Console.WriteLine("? SharpCoreDB (Group Commit FullSync) setup complete");

            // Group Commit - Async
            var groupCommitAsyncPath = Path.Combine(tempDir, "sharpcore_groupcommit_async");
            var asyncConfig = new DatabaseConfig
            {
                NoEncryptMode = true,
                UseGroupCommitWal = true,
                WalDurabilityMode = DurabilityMode.Async,
                WalMaxBatchSize = 500,
                WalMaxBatchDelayMs = 20,
                EnablePageCache = true,
                PageCacheCapacity = 1000,
            };
            sharpCoreDb_GroupCommitAsync = new BenchmarkDatabaseHelper(groupCommitAsyncPath, config: asyncConfig);
            sharpCoreDb_GroupCommitAsync.CreateUsersTable();
            Console.WriteLine("? SharpCoreDB (Group Commit Async) setup complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? SharpCoreDB setup failed: {ex.Message}");
            throw;
        }
    }

    private void SetupSQLite()
    {
        // SQLite in-memory
        sqliteMemory = new SqliteConnection("Data Source=:memory:");
        sqliteMemory.Open();
        CreateSQLiteTable(sqliteMemory);

        // SQLite file with WAL mode
        sqliteFileWalPath = Path.Combine(tempDir, "sqlite_wal.db");
        sqliteFile_WAL = new SqliteConnection($"Data Source={sqliteFileWalPath}");
        sqliteFile_WAL.Open();
        using (var cmd = sqliteFile_WAL.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        CreateSQLiteTable(sqliteFile_WAL);

        // SQLite file without WAL (DELETE mode - traditional)
        sqliteFileNoWalPath = Path.Combine(tempDir, "sqlite_nowal.db");
        sqliteFile_NoWAL = new SqliteConnection($"Data Source={sqliteFileNoWalPath}");
        sqliteFile_NoWAL.Open();
        using (var cmd = sqliteFile_NoWAL.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=DELETE;";
            cmd.ExecuteNonQuery();
        }
        CreateSQLiteTable(sqliteFile_NoWAL);

        Console.WriteLine("? SQLite setup complete (Memory, WAL, No-WAL)");
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

    private void SetupLiteDB()
    {
        liteDbFilePath = Path.Combine(tempDir, "litedb.db");
        liteDb = new LiteDatabase(liteDbFilePath);
        liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
        Console.WriteLine("? LiteDB setup complete");
    }

    // ==================== SHARPCOREDB - LEGACY WAL ====================

    [Benchmark(Description = "SharpCoreDB (Legacy WAL): Sequential Inserts")]
    public async Task<int> SharpCoreDB_LegacyWAL_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        foreach (var user in users)
        {
            try
            {
                int uniqueId = currentBaseId + user.Id;
                sharpCoreDb_LegacyWAL?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                inserted++;
            }
            catch { }
        }

        return inserted;
    }

    [Benchmark(Description = "SharpCoreDB (Legacy WAL): Concurrent Inserts")]
    public async Task<int> SharpCoreDB_LegacyWAL_Concurrent()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var batches = users.Chunk(Math.Max(1, RecordCount / ConcurrentThreads)).ToList();
        
        var tasks = batches.Select(async batch =>
        {
            int count = 0;
            foreach (var user in batch)
            {
                try
                {
                    int uniqueId = currentBaseId + user.Id;
                    sharpCoreDb_LegacyWAL?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                    count++;
                }
                catch { }
            }
            return count;
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    // ==================== SHARPCOREDB - GROUP COMMIT FULLSYNC ====================

    [Benchmark(Description = "SharpCoreDB (GroupCommit FullSync): Sequential Inserts")]
    public async Task<int> SharpCoreDB_GroupCommitFullSync_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        foreach (var user in users)
        {
            try
            {
                int uniqueId = currentBaseId + user.Id;
                sharpCoreDb_GroupCommitFullSync?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                inserted++;
            }
            catch { }
        }

        return inserted;
    }

    [Benchmark(Description = "SharpCoreDB (GroupCommit FullSync): Concurrent Inserts")]
    public async Task<int> SharpCoreDB_GroupCommitFullSync_Concurrent()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var batches = users.Chunk(Math.Max(1, RecordCount / ConcurrentThreads)).ToList();
        
        var tasks = batches.Select(async batch =>
        {
            int count = 0;
            foreach (var user in batch)
            {
                try
                {
                    int uniqueId = currentBaseId + user.Id;
                    sharpCoreDb_GroupCommitFullSync?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                    count++;
                }
                catch { }
            }
            return count;
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    // ==================== SHARPCOREDB - GROUP COMMIT ASYNC ====================

    [Benchmark(Description = "SharpCoreDB (GroupCommit Async): Sequential Inserts")]
    public async Task<int> SharpCoreDB_GroupCommitAsync_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        foreach (var user in users)
        {
            try
            {
                int uniqueId = currentBaseId + user.Id;
                sharpCoreDb_GroupCommitAsync?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                inserted++;
            }
            catch { }
        }

        return inserted;
    }

    [Benchmark(Description = "SharpCoreDB (GroupCommit Async): Concurrent Inserts")]
    public async Task<int> SharpCoreDB_GroupCommitAsync_Concurrent()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        var batches = users.Chunk(Math.Max(1, RecordCount / ConcurrentThreads)).ToList();
        
        var tasks = batches.Select(async batch =>
        {
            int count = 0;
            foreach (var user in batch)
            {
                try
                {
                    int uniqueId = currentBaseId + user.Id;
                    sharpCoreDb_GroupCommitAsync?.InsertUserBenchmark(uniqueId, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                    count++;
                }
                catch { }
            }
            return count;
        });

        var results = await Task.WhenAll(tasks);
        return results.Sum();
    }

    // ==================== SQLITE MEMORY ====================

    [Benchmark(Baseline = true, Description = "SQLite Memory: Sequential Inserts")]
    public int SQLite_Memory_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        using var transaction = sqliteMemory?.BeginTransaction();
        using var cmd = sqliteMemory?.CreateCommand();
        cmd!.CommandText = @"
            INSERT OR REPLACE INTO users (id, name, email, age, created_at, is_active)
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
            inserted++;
        }

        transaction?.Commit();
        return inserted;
    }

    // ==================== SQLITE FILE WITH WAL ====================

    [Benchmark(Description = "SQLite File (WAL): Sequential Inserts")]
    public int SQLite_File_WAL_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        using var transaction = sqliteFile_WAL?.BeginTransaction();
        using var cmd = sqliteFile_WAL?.CreateCommand();
        cmd!.CommandText = @"
            INSERT OR REPLACE INTO users (id, name, email, age, created_at, is_active)
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
            inserted++;
        }

        transaction?.Commit();
        return inserted;
    }

    // ==================== SQLITE FILE WITHOUT WAL ====================

    [Benchmark(Description = "SQLite File (No WAL): Sequential Inserts")]
    public int SQLite_File_NoWAL_Sequential()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;

        using var transaction = sqliteFile_NoWAL?.BeginTransaction();
        using var cmd = sqliteFile_NoWAL?.CreateCommand();
        cmd!.CommandText = @"
            INSERT OR REPLACE INTO users (id, name, email, age, created_at, is_active)
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
            inserted++;
        }

        transaction?.Commit();
        return inserted;
    }

    // ==================== LITEDB ====================

    [Benchmark(Description = "LiteDB: Sequential Inserts")]
    public int LiteDB_Sequential()
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
        Dispose();
    }

    public void Dispose()
    {
        sharpCoreDb_LegacyWAL?.Dispose();
        sharpCoreDb_GroupCommitFullSync?.Dispose();
        sharpCoreDb_GroupCommitAsync?.Dispose();
        sqliteMemory?.Dispose();
        sqliteFile_WAL?.Dispose();
        sqliteFile_NoWAL?.Dispose();
        liteDb?.Dispose();

        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}
