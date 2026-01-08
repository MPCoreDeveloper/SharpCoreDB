// <copyright file="StorageEngineComparisonBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using LiteDB;
using SharpCoreDB.Interfaces;
using System;
using System.IO;

/// <summary>
/// Comprehensive storage engine comparison across all available options.
/// ✅ FIXED: Uses ExecuteBatchSQL for INSERT/UPDATE to avoid potential infinite loops.
/// ✅ RESTORED: All benchmark categories (Insert, Update, Select, Analytics).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class StorageEngineComparisonBenchmark
{
    private const int RecordCount = 5_000; // Records for SELECT/UPDATE benchmarks
    private const int InsertBatchSize = 1_000; // ✅ Smaller batch for INSERT benchmarks
    
    private string appendOnlyPath = string.Empty;
    private string pageBasedPath = string.Empty;
    private string sqlitePath = string.Empty;
    private string liteDbPath = string.Empty;
    
    private IServiceProvider services = null!;
    private Database? appendOnlyDb;
    private Database? pageBasedDb;
    private ColumnStorage.ColumnStore<BenchmarkRecord>? columnarStore;
    private SqliteConnection? sqliteConn;
    private LiteDatabase? liteDb;
    
    // ✅ NEW: Iteration counters for unique IDs
    private int _insertIterationCounter = 0;

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("[GlobalSetup] Starting database initialization...");
        
        appendOnlyPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_appendonly_{Guid.NewGuid()}");
        pageBasedPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_pagebased_{Guid.NewGuid()}");
        sqlitePath = Path.Combine(Path.GetTempPath(), $"sqlite_{Guid.NewGuid()}.db");
        liteDbPath = Path.Combine(Path.GetTempPath(), $"litedb_{Guid.NewGuid()}.db");

        try { if (File.Exists(sqlitePath)) File.Delete(sqlitePath); } catch { }
        try { if (File.Exists(liteDbPath)) File.Delete(liteDbPath); } catch { }

        Directory.CreateDirectory(appendOnlyPath);
        Directory.CreateDirectory(pageBasedPath);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSharpCoreDB();
        services = serviceCollection.BuildServiceProvider();
        var factory = services.GetRequiredService<DatabaseFactory>();

        // AppendOnly config
        var appendOnlyConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            StorageEngineType = StorageEngineType.AppendOnly,
            EnablePageCache = true,
            PageCacheCapacity = 5000,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
            StrictParameterValidation = false
        };

        // PAGE_BASED config
        var pageBasedConfig = new DatabaseConfig
        {
            NoEncryptMode = true,
            StorageEngineType = StorageEngineType.PageBased,
            EnablePageCache = true,
            PageCacheCapacity = 5000,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
            StrictParameterValidation = false
        };

        Console.WriteLine("[GlobalSetup] Creating SharpCoreDB instances...");
        appendOnlyDb = (Database)factory.Create(appendOnlyPath, "password", false, appendOnlyConfig);
        pageBasedDb = (Database)factory.Create(pageBasedPath, "password", false, pageBasedConfig);

        // SQLite setup
        Console.WriteLine("[GlobalSetup] Creating SQLite database...");
        sqliteConn = new SqliteConnection($"Data Source={sqlitePath}");
        sqliteConn.Open();
        using (var cmd = sqliteConn.CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE bench_records (
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
        Console.WriteLine("[GlobalSetup] Creating LiteDB database...");
        var liteMapper = new BsonMapper();
        liteMapper.Entity<BenchmarkRecord>().Id(x => x.Id, autoId: false);
        liteDb = new LiteDatabase(liteDbPath, liteMapper);
        liteDb.GetCollection<BenchmarkRecord>("bench_records").EnsureIndex(x => x.Id);

        // SharpCoreDB schema
        var createTable = @"CREATE TABLE bench_records (
            id INTEGER PRIMARY KEY,
            name TEXT,
            email TEXT,
            age INTEGER,
            salary DECIMAL,
            created DATETIME
        )";

        appendOnlyDb.ExecuteSQL(createTable);
        pageBasedDb.ExecuteSQL(createTable);
        
        // Pre-populate for SELECT/UPDATE benchmarks
        Console.WriteLine("[GlobalSetup] Pre-populating databases...");
        PrePopulateAllDatabases();
        Console.WriteLine("[GlobalSetup] Setup complete!");
    }

    private void PrePopulateAllDatabases()
    {
        Console.WriteLine($"[PrePopulate] Inserting {RecordCount} records into each database...");
        
        // ✅ FIX: Use ExecuteBatchSQL - reliable and tested
        var inserts = new List<string>(RecordCount);
        for (int i = 0; i < RecordCount; i++)
        {
            inserts.Add($@"INSERT INTO bench_records (id, name, email, age, salary, created) 
                VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
        }
        
        Console.WriteLine("[PrePopulate] Inserting into AppendOnly...");
        appendOnlyDb!.ExecuteBatchSQL(inserts);
        
        Console.WriteLine("[PrePopulate] Inserting into PageBased...");
        pageBasedDb!.ExecuteBatchSQL(inserts);

        // Pre-populate SQLite
        Console.WriteLine("[PrePopulate] Inserting into SQLite...");
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "INSERT INTO bench_records (id, name, email, age, salary, created) VALUES (@id, @name, @email, @age, @salary, @created)";
        
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

        // Pre-populate LiteDB
        Console.WriteLine("[PrePopulate] Inserting into LiteDB...");
        var liteCollection = liteDb!.GetCollection<BenchmarkRecord>("bench_records");
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
        liteCollection.InsertBulk(records);

        // Pre-transpose for SIMD benchmarks
        Console.WriteLine("[PrePopulate] Creating columnar store for SIMD benchmarks...");
        columnarStore = new ColumnStorage.ColumnStore<BenchmarkRecord>();
        columnarStore.Transpose(records);
        
        Console.WriteLine($"[PrePopulate] Complete! {RecordCount} records in each database.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine("[GlobalCleanup] Disposing databases...");
        appendOnlyDb?.Dispose();
        pageBasedDb?.Dispose();
        sqliteConn?.Dispose();
        liteDb?.Dispose();

        try
        {
            if (Directory.Exists(appendOnlyPath)) Directory.Delete(appendOnlyPath, true);
            if (Directory.Exists(pageBasedPath)) Directory.Delete(pageBasedPath, true);
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
            if (File.Exists(liteDbPath)) File.Delete(liteDbPath);
        }
        catch { }
        Console.WriteLine("[GlobalCleanup] Cleanup complete!");
    }

    /// <summary>
    /// Increment counter for unique INSERT IDs per iteration.
    /// </summary>
    [IterationSetup(Targets = new[] { 
        nameof(AppendOnly_Insert), 
        nameof(PageBased_Insert),
        nameof(SQLite_Insert),
        nameof(LiteDB_Insert)
    })]
    public void InsertIterationSetup()
    {
        _insertIterationCounter++;
    }

    // ============================================================
    // INSERT BENCHMARKS (1K records per iteration)
    // ============================================================

    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void AppendOnly_Insert()
    {
        int startId = RecordCount + (_insertIterationCounter * InsertBatchSize);
        var inserts = new List<string>(InsertBatchSize);
        for (int i = 0; i < InsertBatchSize; i++)
        {
            int id = startId + i;
            inserts.Add($@"INSERT INTO bench_records (id, name, email, age, salary, created) 
                VALUES ({id}, 'NewUser{id}', 'newuser{id}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
        }
        appendOnlyDb!.ExecuteBatchSQL(inserts);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Insert")]
    public void PageBased_Insert()
    {
        int startId = RecordCount + (_insertIterationCounter * InsertBatchSize);
        var inserts = new List<string>(InsertBatchSize);
        for (int i = 0; i < InsertBatchSize; i++)
        {
            int id = startId + i;
            inserts.Add($@"INSERT INTO bench_records (id, name, email, age, salary, created) 
                VALUES ({id}, 'NewUser{id}', 'newuser{id}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
        }
        pageBasedDb!.ExecuteBatchSQL(inserts);
    }

    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void SQLite_Insert()
    {
        int startId = RecordCount + (_insertIterationCounter * InsertBatchSize);
        
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "INSERT INTO bench_records (id, name, email, age, salary, created) VALUES (@id, @name, @email, @age, @salary, @created)";
        
        var idParam = cmd.Parameters.Add("@id", SqliteType.Integer);
        var nameParam = cmd.Parameters.Add("@name", SqliteType.Text);
        var emailParam = cmd.Parameters.Add("@email", SqliteType.Text);
        var ageParam = cmd.Parameters.Add("@age", SqliteType.Integer);
        var salaryParam = cmd.Parameters.Add("@salary", SqliteType.Real);
        var createdParam = cmd.Parameters.Add("@created", SqliteType.Text);

        for (int i = 0; i < InsertBatchSize; i++)
        {
            int id = startId + i;
            idParam.Value = id;
            nameParam.Value = $"NewUser{id}";
            emailParam.Value = $"newuser{id}@test.com";
            ageParam.Value = 20 + (i % 50);
            salaryParam.Value = 30000 + (i % 70000);
            createdParam.Value = "2025-01-01";
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    [Benchmark]
    [BenchmarkCategory("Insert")]
    public void LiteDB_Insert()
    {
        int startId = RecordCount + (_insertIterationCounter * InsertBatchSize);
        var collection = liteDb!.GetCollection<BenchmarkRecord>("bench_records");
        
        var records = new List<BenchmarkRecord>(InsertBatchSize);
        for (int i = 0; i < InsertBatchSize; i++)
        {
            int id = startId + i;
            records.Add(new BenchmarkRecord
            {
                Id = id,
                Name = $"NewUser{id}",
                Email = $"newuser{id}@test.com",
                Age = 20 + (i % 50),
                Salary = 30000 + (i % 70000),
                Created = DateTime.Parse("2025-01-01")
            });
        }
        collection.InsertBulk(records);
    }

    // ============================================================
    // UPDATE BENCHMARKS (500 random updates)
    // ============================================================

    [Benchmark]
    [BenchmarkCategory("Update")]
    public void AppendOnly_Update()
    {
        var updates = new List<string>(500);
        for (int i = 0; i < 500; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            updates.Add($"UPDATE bench_records SET salary = {50000 + id} WHERE id = {id}");
        }
        appendOnlyDb!.ExecuteBatchSQL(updates);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Update")]
    public void PageBased_Update()
    {
        var updates = new List<string>(500);
        for (int i = 0; i < 500; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            updates.Add($"UPDATE bench_records SET salary = {50000 + id} WHERE id = {id}");
        }
        pageBasedDb!.ExecuteBatchSQL(updates);
    }

    [Benchmark]
    [BenchmarkCategory("Update")]
    public void SQLite_Update()
    {
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "UPDATE bench_records SET salary = @salary WHERE id = @id";
        
        var salaryParam = cmd.Parameters.Add("@salary", SqliteType.Real);
        var idParam = cmd.Parameters.Add("@id", SqliteType.Integer);

        for (int i = 0; i < 500; i++)
        {
            var id = Random.Shared.Next(0, RecordCount);
            idParam.Value = id;
            salaryParam.Value = 50000 + id;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    [Benchmark]
    [BenchmarkCategory("Update")]
    public void LiteDB_Update()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("bench_records");
        
        for (int i = 0; i < 500; i++)
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
    // SELECT BENCHMARKS
    // ============================================================

    [Benchmark]
    [BenchmarkCategory("Select")]
    public void AppendOnly_Select()
    {
        var rows = appendOnlyDb!.ExecuteQuery("SELECT * FROM bench_records WHERE age > 30");
        _ = rows.Count;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Select")]
    public void PageBased_Select()
    {
        var rows = pageBasedDb!.ExecuteQuery("SELECT * FROM bench_records WHERE age > 30");
        _ = rows.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Select")]
    public void SQLite_Select()
    {
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM bench_records WHERE age > 30";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) count++;
        _ = count;
    }

    [Benchmark]
    [BenchmarkCategory("Select")]
    public void LiteDB_Select()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("bench_records");
        var results = collection.Find(x => x.Age > 30);
        _ = results.Count();
    }

    // ============================================================
    // ANALYTICS BENCHMARKS (SIMD-accelerated)
    // ============================================================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Analytics")]
    public void Columnar_SIMD_Sum()
    {
        if (columnarStore == null || columnarStore.RowCount == 0) return;
        
        var totalSalary = columnarStore!.Sum<decimal>("Salary");
        var avgAge = columnarStore.Average("Age");
        _ = totalSalary;
        _ = avgAge;
    }

    [Benchmark]
    [BenchmarkCategory("Analytics")]
    public void SQLite_Sum()
    {
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT SUM(salary), AVG(age) FROM bench_records";
        using var reader = cmd.ExecuteReader();
        reader.Read();
        _ = reader.GetDouble(0);
        _ = reader.GetDouble(1);
    }

    [Benchmark]
    [BenchmarkCategory("Analytics")]
    public void LiteDB_Sum()
    {
        var collection = liteDb!.GetCollection<BenchmarkRecord>("bench_records");
        var allRecords = collection.FindAll().ToList();
        _ = allRecords.Sum(x => x.Salary);
        _ = allRecords.Average(x => x.Age);
    }

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
