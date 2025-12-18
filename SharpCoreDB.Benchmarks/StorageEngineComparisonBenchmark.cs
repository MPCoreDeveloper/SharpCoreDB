// <copyright file="StorageEngineComparisonBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using LiteDB;
using SharpCoreDB.Interfaces;
using System;
using System.IO;

/// <summary>
/// Comprehensive storage engine comparison across all available options.
/// Compares:
/// - SharpCoreDB AppendOnly (columnar)
/// - SharpCoreDB PAGE_BASED (optimized)
/// - SQLite (industry standard)
/// - LiteDB (pure .NET competitor)
/// 
/// Test scale: 100K records to validate production performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class StorageEngineComparisonBenchmark
{
    private const int RecordCount = 100_000;
    
    private string appendOnlyPath = string.Empty;
    private string pageBasedPath = string.Empty;
    private string sqlitePath = string.Empty;
    private string liteDbPath = string.Empty;
    
    private IServiceProvider services = null!;
    private Database? appendOnlyDb; // ? Changed to Database for Dispose
    private Database? pageBasedDb; // ? Changed to Database for Dispose
    private SqliteConnection? sqliteConn;
    private LiteDatabase? liteDb;

    /// <summary>
    /// Setup all databases with identical schema.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        appendOnlyPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_appendonly_{Guid.NewGuid()}");
        pageBasedPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_pagebased_{Guid.NewGuid()}");
        sqlitePath = Path.Combine(Path.GetTempPath(), $"sqlite_{Guid.NewGuid()}.db");
        liteDbPath = Path.Combine(Path.GetTempPath(), $"litedb_{Guid.NewGuid()}.db");

        Directory.CreateDirectory(appendOnlyPath);
        Directory.CreateDirectory(pageBasedPath);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSharpCoreDB();
        services = serviceCollection.BuildServiceProvider();
        var factory = services.GetRequiredService<DatabaseFactory>();

        // AppendOnly config (columnar)
        var appendOnlyConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            StorageEngineType = StorageEngineType.AppendOnly,
            EnablePageCache = false
        };

        // PAGE_BASED config (fully optimized)
        var pageBasedConfig = DatabaseConfig.OLTP; // Uses optimized PAGE_BASED

        appendOnlyDb = (Database)factory.Create(appendOnlyPath, "password", isReadOnly: false, appendOnlyConfig);
        pageBasedDb = (Database)factory.Create(pageBasedPath, "password", isReadOnly: false, pageBasedConfig);

        // SQLite setup
        sqliteConn = new SqliteConnection($"Data Source={sqlitePath}");
        sqliteConn.Open();
        using (var cmd = sqliteConn.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE benchmark (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                salary REAL,
                created TEXT
            )";
            cmd.ExecuteNonQuery();
        }

        // LiteDB setup
        liteDb = new LiteDatabase(liteDbPath);
        var collection = liteDb.GetCollection<BenchmarkRecord>("benchmark");
        collection.EnsureIndex(x => x.Id);

        // SharpCoreDB schema
        var createTable = @"CREATE TABLE benchmark (
            id INTEGER PRIMARY KEY,
            name TEXT,
            email TEXT,
            age INTEGER,
            salary DECIMAL,
            created DATETIME
        )";

        appendOnlyDb.ExecuteSQL(createTable);
        pageBasedDb.ExecuteSQL(createTable);
    }

    /// <summary>
    /// Cleanup all databases.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        appendOnlyDb?.Dispose();
        pageBasedDb?.Dispose();
        sqliteConn?.Dispose();
        liteDb?.Dispose();

        try
        {
            if (Directory.Exists(appendOnlyPath)) Directory.Delete(appendOnlyPath, recursive: true);
            if (Directory.Exists(pageBasedPath)) Directory.Delete(pageBasedPath, recursive: true);
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
            if (File.Exists(liteDbPath)) File.Delete(liteDbPath);
        }
        catch { /* Ignore */ }
    }

    // ============================================================
    // INSERT BENCHMARKS (100K records)
    // ============================================================

    /// <summary>
    /// AppendOnly INSERT: Expected ~500-700ms (good sequential write performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void AppendOnly_Insert_100K()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            appendOnlyDb!.ExecuteSQL($@"INSERT INTO benchmark (id, name, email, age, salary, created) 
                VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
        }
    }

    /// <summary>
    /// PAGE_BASED INSERT (optimized): Expected ~200-300ms (3-5x faster than AppendOnly).
    /// Target: 300-500 ops/ms throughput.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Insert")]
    public void PageBased_Insert_100K()
    {
        for (int i = 0; i < RecordCount; i++)
        {
            pageBasedDb!.ExecuteSQL($@"INSERT INTO benchmark (id, name, email, age, salary, created) 
                VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
        }
    }

    /// <summary>
    /// SQLite INSERT: Expected ~40-60ms (industry-leading sequential insert performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void SQLite_Insert_100K()
    {
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "INSERT INTO benchmark (id, name, email, age, salary, created) VALUES (@id, @name, @email, @age, @salary, @created)";
        
        var idParam = cmd.Parameters.Add("@id", SqliteType.Integer);
        var nameParam = cmd.Parameters.Add("@name", SqliteType.Text);
        var emailParam = cmd.Parameters.Add("@email", SqliteType.Text);
        var ageParam = cmd.Parameters.Add("@age", SqliteType.Integer);
        var salaryParam = cmd.Parameters.Add("@salary", SqliteType.Real);
        var createdParam = cmd.Parameters.Add("@created", SqliteType.Text);

        for (int i = 0; i < RecordCount; i++)
        {
            idParam.Value = i;
            nameParam.Value = $"User{i}";
            emailParam.Value = $"user{i}@test.com";
            ageParam.Value = 20 + (i % 50);
            salaryParam.Value = 30000 + (i % 70000);
            createdParam.Value = "2025-01-01";
            cmd.ExecuteNonQuery();
        }
        
        transaction.Commit();
    }

    /// <summary>
    /// LiteDB INSERT: Expected ~120-180ms (good pure .NET performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void LiteDB_Insert_100K()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("benchmark");
        var records = new List<BenchmarkRecord>(RecordCount);
        
        for (int i = 0; i < RecordCount; i++)
        {
            records.Add(new BenchmarkRecord
            {
                Id = i,
                Name = $"User{i}",
                Email = $"user{i}@test.com",
                Age = 20 + (i % 50),
                Salary = 30000 + (i % 70000),
                Created = DateTime.Parse("2025-01-01")
            });
        }
        
        collection.InsertBulk(records);
    }

    // ============================================================
    // UPDATE BENCHMARKS (50K random updates)
    // ============================================================

    /// <summary>
    /// AppendOnly UPDATE: Expected ~400-600ms (slow - append-only rewrites records).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Update")]
    public void AppendOnly_Update_50K()
    {
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            appendOnlyDb!.ExecuteSQL($"UPDATE benchmark SET salary = {50000 + id} WHERE id = {id}");
        }
    }

    /// <summary>
    /// PAGE_BASED UPDATE (optimized): Expected ~120-180ms (3-5x faster due to in-place updates + LRU cache).
    /// Target: 250-400 ops/ms throughput.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Update")]
    public void PageBased_Update_50K()
    {
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            pageBasedDb!.ExecuteSQL($"UPDATE benchmark SET salary = {50000 + id} WHERE id = {id}");
        }
    }

    /// <summary>
    /// SQLite UPDATE: Expected ~80-120ms (excellent random update performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Update")]
    public void SQLite_Update_50K()
    {
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "UPDATE benchmark SET salary = @salary WHERE id = @id";
        
        var salaryParam = cmd.Parameters.Add("@salary", SqliteType.Real);
        var idParam = cmd.Parameters.Add("@id", SqliteType.Integer);

        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            idParam.Value = id;
            salaryParam.Value = 50000 + id;
            cmd.ExecuteNonQuery();
        }
        
        transaction.Commit();
    }

    /// <summary>
    /// LiteDB UPDATE: Expected ~180-250ms (good document database update performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Update")]
    public void LiteDB_Update_50K()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("benchmark");
        
        for (int i = 0; i < RecordCount / 2; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            var record = collection.FindById(id);
            if (record is not null)
            {
                record.Salary = 50000 + id;
                collection.Update(record);
            }
        }
    }

    // ============================================================
    // SELECT BENCHMARKS (full table scan)
    // ============================================================

    /// <summary>
    /// AppendOnly SELECT: Expected ~100-150ms (good sequential scan performance).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Select")]
    public void AppendOnly_Select_FullScan()
    {
        appendOnlyDb!.ExecuteSQL("SELECT * FROM benchmark WHERE age > 30"); // ? Returns void
    }

    /// <summary>
    /// PAGE_BASED SELECT (with LRU cache): Expected ~20-40ms first run, <5ms on cache hit.
    /// Target: 5-10x faster than baseline on hot data.
    /// </summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Select")]
    public void PageBased_Select_FullScan()
    {
        pageBasedDb!.ExecuteSQL("SELECT * FROM benchmark WHERE age > 30"); // ? Returns void
    }

    /// <summary>
    /// SQLite SELECT: Expected ~30-50ms (excellent scan performance with B-tree index).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Select")]
    public void SQLite_Select_FullScan()
    {
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM benchmark WHERE age > 30";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        _ = count;
    }

    /// <summary>
    /// LiteDB SELECT: Expected ~80-120ms (document scan with filtering).
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Select")]
    public void LiteDB_Select_FullScan()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("benchmark");
        var results = collection.Find(x => x.Age > 30);
        _ = results.Count();
    }

    /// <summary>
    /// LiteDB record model for benchmarking.
    /// </summary>
    private class BenchmarkRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Age { get; set; }
        public decimal Salary { get; set; }
        public DateTime Created { get; set; }
    }
}
