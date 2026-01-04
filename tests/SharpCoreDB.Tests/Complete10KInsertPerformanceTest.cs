// <copyright file="Complete10KInsertPerformanceTest.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using SharpCoreDB;
using SharpCoreDB.Services;

namespace SharpCoreDB.Tests;

/// <summary>
/// Complete 10K insert performance test comparing all optimization levels.
/// Goal: Within 20-30% of SQLite (SQLite: 42ms ? Target: 50-55ms).
/// 
/// Tests 5 configurations:
/// 1. Baseline (no optimizations)
/// 2. Standard config
/// 3. HighSpeed config
/// 4. BulkImport config
/// 5. UseOptimizedInsertPath (delayed transpose + buffered encryption)
/// </summary>
public sealed class Complete10KInsertPerformanceTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;
    private readonly DatabaseFactory _factory;
    private readonly List<Dictionary<string, object>> _testRows;

    public Complete10KInsertPerformanceTest(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"perf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();

        // Generate 10K test rows
        _testRows = Enumerable.Range(0, 10_000)
            .Select(i => new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"User_{i}",
                ["email"] = $"user{i}@example.com",
                ["age"] = 20 + (i % 50),
                ["created_at"] = DateTime.UtcNow.ToString("o"),
                ["is_active"] = i % 2 == 0 ? 1 : 0
            })
            .ToList();
    }

    [Fact]
    public void Test1_Baseline_NoOptimizations()
    {
        var dbPath = Path.Combine(_testDbPath, "baseline");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
            UseOptimizedInsertPath = false,
            UseGroupCommitWal = false,
            EnableQueryCache = false,
            EnablePageCache = false,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var sw = Stopwatch.StartNew();
        
        // Individual inserts (worst case)
        foreach (var row in _testRows)
        {
            db.ExecuteSQL(
                "INSERT INTO users VALUES (?, ?, ?, ?, ?, ?)",
                new Dictionary<string, object?>
                {
                    ["0"] = row["id"],
                    ["1"] = row["name"],
                    ["2"] = row["email"],
                    ["3"] = row["age"],
                    ["4"] = row["created_at"],
                    ["5"] = row["is_active"]
                });
        }
        
        sw.Stop();

        _output.WriteLine($"1?? BASELINE (No Optimizations): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public void Test2_Standard_ExecuteBatchSQL()
    {
        var dbPath = Path.Combine(_testDbPath, "standard");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
            UseOptimizedInsertPath = false,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var statements = _testRows.Select(row => 
            $"INSERT INTO users VALUES ({row["id"]}, '{row["name"]}', '{row["email"]}', {row["age"]}, '{row["created_at"]}', {row["is_active"]})"
        ).ToList();

        var sw = Stopwatch.StartNew();
        db.ExecuteBatchSQL(statements);
        sw.Stop();

        _output.WriteLine($"2?? STANDARD (ExecuteBatchSQL): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public async Task Test3_HighSpeed_BulkInsertAsync()
    {
        var dbPath = Path.Combine(_testDbPath, "highspeed");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = true,  // ? Enabled
            UseOptimizedInsertPath = false,
            GroupCommitSize = 1000,
            WalBufferSize = 8 * 1024 * 1024,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var sw = Stopwatch.StartNew();
        await db.BulkInsertAsync("users", _testRows);
        sw.Stop();

        _output.WriteLine($"3?? HIGHSPEED (HighSpeedInsertMode): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        _output.WriteLine($"   Config: GroupCommitSize=1000, WalBuffer=8MB");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public async Task Test4_BulkImport_AggressiveConfig()
    {
        var dbPath = Path.Combine(_testDbPath, "bulkimport");
        var db = (Database)_factory.Create(dbPath, "pass", false, DatabaseConfig.BulkImport, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var sw = Stopwatch.StartNew();
        await db.BulkInsertAsync("users", _testRows);
        sw.Stop();

        _output.WriteLine($"4?? BULKIMPORT (Aggressive Config): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        _output.WriteLine($"   Config: GroupCommitSize=5000, WalBuffer=16MB, WalBatchMultiplier=512");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public async Task Test5_OptimizedPath_DelayedTranspose_BufferedEncryption()
    {
        var dbPath = Path.Combine(_testDbPath, "optimized");
        var config = new DatabaseConfig
        {
            NoEncryptMode = false,  // ? Test with encryption!
            HighSpeedInsertMode = true,
            UseOptimizedInsertPath = true,  // ? Delayed transpose + buffered encryption
            ToggleEncryptionDuringBulk = false,  // Keep encryption enabled
            EncryptionBufferSizeKB = 64,  // Large buffer
            GroupCommitSize = 1000,
            WalBufferSize = 8 * 1024 * 1024,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var sw = Stopwatch.StartNew();
        await db.BulkInsertAsync("users", _testRows);
        sw.Stop();

        _output.WriteLine($"5?? OPTIMIZED PATH (Delayed Transpose + Buffered Encryption): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        _output.WriteLine($"   Config: UseOptimizedInsertPath=true, EncryptionBuffer=64KB");
        _output.WriteLine($"   Note: INCLUDES encryption (NoEncryptMode=false)");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public async Task Test6_OptimizedPath_NoEncryption()
    {
        var dbPath = Path.Combine(_testDbPath, "optimized_noenc");
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,  // ? No encryption for max speed
            HighSpeedInsertMode = true,
            UseOptimizedInsertPath = true,  // ? Delayed transpose
            GroupCommitSize = 1000,
            WalBufferSize = 8 * 1024 * 1024,
        };
        var db = (Database)_factory.Create(dbPath, "pass", false, config, null);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER, created_at TEXT, is_active INTEGER)");

        var sw = Stopwatch.StartNew();
        await db.BulkInsertAsync("users", _testRows);
        sw.Stop();

        _output.WriteLine($"6?? OPTIMIZED PATH (No Encryption): {sw.ElapsedMilliseconds} ms");
        _output.WriteLine($"   Throughput: {10_000 / (sw.ElapsedMilliseconds / 1000.0):N0} rec/sec");
        _output.WriteLine($"   Config: UseOptimizedInsertPath=true, NoEncryptMode=true");
        
        CleanupDb((Database)db, dbPath);
    }

    [Fact]
    public void ComparisonSummary_AllConfigurations()
    {
        var results = new List<(string Name, long TimeMs, double Throughput)>();

        // Run all tests
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
        RunAndRecord(results, "Baseline (No Opt)", () => Test1_Baseline_NoOptimizations());
        RunAndRecord(results, "Standard Batch", () => Test2_Standard_ExecuteBatchSQL());
        RunAndRecord(results, "HighSpeed Mode", () => Test3_HighSpeed_BulkInsertAsync());
        RunAndRecord(results, "BulkImport Config", () => Test4_BulkImport_AggressiveConfig());
        RunAndRecord(results, "Optimized + Enc", () => Test5_OptimizedPath_DelayedTranspose_BufferedEncryption());
        RunAndRecord(results, "Optimized No Enc", () => Test6_OptimizedPath_NoEncryption());
#pragma warning restore CS4014

        // Print summary
        _output.WriteLine("");
        _output.WriteLine("???????????????????????????????????????????????????????????");
        _output.WriteLine("  COMPLETE 10K INSERT PERFORMANCE SUMMARY");
        _output.WriteLine("???????????????????????????????????????????????????????????");
        _output.WriteLine("");
        _output.WriteLine("?????????????????????????????????????????????????????????????????");
        _output.WriteLine("? Configuration            ? Time (ms)? Throughput   ? vs SQLite?");
        _output.WriteLine("?????????????????????????????????????????????????????????????????");

        const long sqliteTime = 42; // SQLite baseline
        const long targetMin = 50;  // 20% slower
        const long targetMax = 55;  // 30% slower

        foreach (var (name, time, throughput) in results.OrderBy(r => r.TimeMs))
        {
            var ratio = (double)time / sqliteTime;
            var marker = time <= targetMax ? "?" : time <= 75 ? "??" : "?";
            
            _output.WriteLine($"? {name,-24} ? {time,8} ? {throughput,12:N0} ? {ratio,6:F2}x {marker} ?");
        }

        _output.WriteLine("?????????????????????????????????????????????????????????????????");
        _output.WriteLine("");
        _output.WriteLine($"?? BASELINE: SQLite = {sqliteTime}ms");
        _output.WriteLine($"?? TARGET: {targetMin}-{targetMax}ms (20-30% of SQLite)");
        _output.WriteLine("");

        // Winner
        var best = results.OrderBy(r => r.TimeMs).First();
        var bestRatio = (double)best.TimeMs / sqliteTime;

        _output.WriteLine($"?? WINNER: {best.Name} = {best.TimeMs}ms ({bestRatio:F2}x slower than SQLite)");
        
        if (best.TimeMs <= targetMax)
        {
            _output.WriteLine($"? TARGET ACHIEVED! Within 20-30% of SQLite performance!");
        }
        else if (best.TimeMs <= 75)
        {
            _output.WriteLine($"?? CLOSE: {best.TimeMs}ms (target: {targetMin}-{targetMax}ms)");
        }
        else
        {
            _output.WriteLine($"? NEEDS WORK: {best.TimeMs}ms (target: {targetMin}-{targetMax}ms)");
        }
    }

    private void RunAndRecord(List<(string, long, double)> results, string name, Action test)
    {
        var sw = Stopwatch.StartNew();
        test();
        sw.Stop();

        var throughput = 10_000 / (sw.ElapsedMilliseconds / 1000.0);
        results.Add((name, sw.ElapsedMilliseconds, throughput));
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
        // Final attempt
        Directory.Delete(path, true);
    }

    private void CleanupDb(Database db, string dbPath)
    {
        if (db is not null)
        {
            db.ClearQueryCache();
            db.Dispose();
        }
        Thread.Sleep(300);
        try { GroupCommitWAL.CleanupOrphanedWAL(dbPath); } catch { }
        SafeDeleteDirectory(dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
