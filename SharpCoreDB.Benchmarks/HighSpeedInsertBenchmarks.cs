// <copyright file="HighSpeedInsertBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for HighSpeedInsert mode comparing:
/// - ExecuteBatchSQL (current implementation)
/// - BulkInsertAsync with standard config
/// - BulkInsertAsync with HighSpeedInsertMode
/// - BulkInsertAsync with BulkImport config
/// 
/// Target: Validate 2-4x speedup for BulkInsertAsync with HighSpeedInsertMode.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class HighSpeedInsertBenchmarks : IDisposable
{
    private TestDataGenerator _dataGenerator = null!;
    private string _tempDir = null!;
    private IServiceProvider _serviceProvider = null!;
    private DatabaseFactory _factory = null!;
    
    // Test data
    private List<Dictionary<string, object>> _1kRows = null!;
    private List<Dictionary<string, object>> _10kRows = null!;
    private List<Dictionary<string, object>> _100kRows = null!;
    
    private int _currentBaseId = 0;

    [Params(1000, 10000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dataGenerator = new TestDataGenerator();
        _tempDir = Path.Combine(Path.GetTempPath(), $"highSpeedBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        // Setup DI
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();

        // Pre-generate test data
        _1kRows = GenerateRows(1_000);
        _10kRows = GenerateRows(10_000);
        _100kRows = GenerateRows(100_000);

        Console.WriteLine("? HighSpeedInsert benchmarks setup complete");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Increment base ID to avoid conflicts
        _currentBaseId += 1_000_000;
    }

    private List<Dictionary<string, object>> GenerateRows(int count)
    {
        var users = _dataGenerator.GenerateUsers(count);
        return users.Select(u => new Dictionary<string, object>
        {
            ["id"] = u.Id,
            ["name"] = u.Name,
            ["email"] = u.Email,
            ["age"] = u.Age,
            ["created_at"] = u.CreatedAt.ToString("o"),
            ["is_active"] = u.IsActive ? 1 : 0
        }).ToList();
    }

    private List<Dictionary<string, object>> GetRows()
    {
        return RecordCount switch
        {
            1000 => _1kRows,
            10000 => _10kRows,
            100000 => _100kRows,
            _ => GenerateRows(RecordCount)
        };
    }

    // ==================== BASELINE: ExecuteBatchSQL ====================

    [Benchmark(Baseline = true, Description = "ExecuteBatchSQL (Baseline)")]
    public async Task ExecuteBatchSQL_Baseline()
    {
        var dbPath = Path.Combine(_tempDir, $"batch_baseline_{Guid.NewGuid()}");
        var db = (Database)_factory.Create(dbPath, "pass", false, DatabaseConfig.Benchmark, null);
        
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");
        
        var rows = GetRows();
        var statements = new List<string>(rows.Count);
        
        foreach (var row in rows)
        {
            int id = _currentBaseId + (int)row["id"];
            statements.Add($@"
                INSERT INTO users (id, name, email, age, created_at, is_active) 
                VALUES ({id}, '{row["name"]?.ToString()?.Replace("'", "''")}', '{row["email"]?.ToString()?.Replace("'", "''")}', {row["age"]}, '{row["created_at"]}', {row["is_active"]})");
        }
        
        await db.ExecuteBatchSQLAsync(statements);
        
        // Cleanup
        Directory.Delete(dbPath, true);
    }

    // ==================== TEST 1: BulkInsertAsync (Standard Config) ====================

    [Benchmark(Description = "BulkInsertAsync (Standard Config)")]
    public async Task BulkInsertAsync_Standard()
    {
        var dbPath = Path.Combine(_tempDir, $"bulk_standard_{Guid.NewGuid()}");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,  // ? Disabled
            UseGroupCommitWal = false,
            EnableQueryCache = false,
            EnablePageCache = false,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);
        
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");
        
        var rows = GetRows().Select(r => new Dictionary<string, object>
        {
            ["id"] = _currentBaseId + (int)r["id"],
            ["name"] = r["name"]!,
            ["email"] = r["email"]!,
            ["age"] = r["age"]!,
            ["created_at"] = r["created_at"]!,
            ["is_active"] = r["is_active"]!
        }).ToList();
        
        await db.BulkInsertAsync("users", rows);
        
        // Cleanup
        Directory.Delete(dbPath, true);
    }

    // ==================== TEST 2: BulkInsertAsync (HighSpeedInsert Mode) ====================

    [Benchmark(Description = "BulkInsertAsync (HighSpeedInsert Mode)")]
    public async Task BulkInsertAsync_HighSpeed()
    {
        var dbPath = Path.Combine(_tempDir, $"bulk_highspeed_{Guid.NewGuid()}");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = true,   // ? Enabled
            UseGroupCommitWal = false,
            GroupCommitSize = 1000,       // Batch 1000 rows at a time
            EnableQueryCache = false,
            EnablePageCache = false,
            WalBufferSize = 8 * 1024 * 1024,  // 8MB buffer
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);
        
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");
        
        var rows = GetRows().Select(r => new Dictionary<string, object>
        {
            ["id"] = _currentBaseId + (int)r["id"],
            ["name"] = r["name"]!,
            ["email"] = r["email"]!,
            ["age"] = r["age"]!,
            ["created_at"] = r["created_at"]!,
            ["is_active"] = r["is_active"]!
        }).ToList();
        
        await db.BulkInsertAsync("users", rows);
        
        // Cleanup
        Directory.Delete(dbPath, true);
    }

    // ==================== TEST 3: BulkInsertAsync (BulkImport Config) ====================

    [Benchmark(Description = "BulkInsertAsync (BulkImport Config)")]
    public async Task BulkInsertAsync_BulkImport()
    {
        var dbPath = Path.Combine(_tempDir, $"bulk_import_{Guid.NewGuid()}");
        var db = (Database)_factory.Create(dbPath, "pass", false, DatabaseConfig.BulkImport, null);
        
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");
        
        var rows = GetRows().Select(r => new Dictionary<string, object>
        {
            ["id"] = _currentBaseId + (int)r["id"],
            ["name"] = r["name"]!,
            ["email"] = r["email"]!,
            ["age"] = r["age"]!,
            ["created_at"] = r["created_at"]!,
            ["is_active"] = r["is_active"]!
        }).ToList();
        
        await db.BulkInsertAsync("users", rows);
        
        // Cleanup
        Directory.Delete(dbPath, true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
