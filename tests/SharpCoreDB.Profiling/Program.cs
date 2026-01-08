// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SharpCoreDB.Profiling;

/// <summary>
/// Performance profiling harness for insert pipeline analysis.
/// Generates 10K inserts in hybrid and page-based modes for profiling.
/// 
/// Usage:
///   Visual Studio: Debug → Performance Profiler (Alt+F2)
///   CLI: dotnet run --project SharpCoreDB.Profiling [page-based|columnar|compare|continuous|encrypted-select]
/// </summary>
internal class Program
{
    private const int RecordCount = 10_000;
    private const int WarmupRuns = 3;
    
    static void Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  SharpCoreDB Insert Pipeline Profiler");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        Console.WriteLine($"Process ID: {Environment.ProcessId}");
        Console.WriteLine($"Records to insert: {RecordCount:N0}");
        Console.WriteLine();
        
        // ✅ NEW: Support command-line arguments for VS launch profiles
        string mode = args.Length > 0 ? args[0].ToLower() : "menu";
        
        switch (mode)
        {
            case "page-based":
            case "pagebased":
            case "1":
                Console.WriteLine("🔧 Running PAGE_BASED profiling mode (from launch profile)");
                Console.WriteLine();
                ProfilePageBasedMode();
                break;
                
            case "columnar":
            case "2":
                Console.WriteLine("🔧 Running COLUMNAR profiling mode (from launch profile)");
                Console.WriteLine();
                ProfileColumnarMode();
                break;
                
            case "compare":
            case "3":
                Console.WriteLine("🔧 Running COMPARATIVE profiling mode (from launch profile)");
                Console.WriteLine();
                ProfilePageBasedMode();
                Console.WriteLine();
                ProfileColumnarMode();
                break;
                
            case "continuous":
            case "4":
                Console.WriteLine("🔧 Running CONTINUOUS profiling mode (from launch profile)");
                Console.WriteLine();
                ContinuousProfiling();
                break;
            
            case "encrypted-select":
            case "encrypted":
            case "5":
                Console.WriteLine("🔧 Running ENCRYPTED SELECT debug mode");
                Console.WriteLine();
                DebugEncryptedSelect();
                break;
                
            default:
                // Interactive menu mode (original behavior)
                ShowMenuAndExecute();
                break;
        }
        
        Console.WriteLine();
        Console.WriteLine("✅ Profiling complete!");
        Console.WriteLine("📊 Visual Studio: View results in Performance Profiler tab");
        Console.WriteLine();
    }
    
    static void ShowMenuAndExecute()
    {
        Console.WriteLine("Select profiling scenario:");
        Console.WriteLine("  1) PAGE_BASED Mode (default - OLTP workload)");
        Console.WriteLine("  2) COLUMNAR Mode (append-only - OLAP workload)");
        Console.WriteLine("  3) Both modes for comparison");
        Console.WriteLine("  4) Continuous profiling (runs until Ctrl+C)");
        Console.WriteLine("  5) Encrypted SELECT debug");
        Console.WriteLine();
        Console.Write("Choice [1]: ");
        
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) input = "1";
        
        Console.WriteLine();
        Console.WriteLine("🔬 Starting profiling...");
        Console.WriteLine("  TIP: Use Debug → Performance Profiler (Alt+F2) in Visual Studio");
        Console.WriteLine();
        
        switch (input)
        {
            case "1":
                ProfilePageBasedMode();
                break;
            case "2":
                ProfileColumnarMode();
                break;
            case "3":
                ProfilePageBasedMode();
                Console.WriteLine();
                ProfileColumnarMode();
                break;
            case "4":
                ContinuousProfiling();
                break;
            case "5":
                DebugEncryptedSelect();
                break;
            default:
                Console.WriteLine("Invalid choice.");
                break;
        }
    }

    /// <summary>
    /// ✅ NEW: Debug encrypted PageBased SELECT to find the crash cause
    /// </summary>
    static void DebugEncryptedSelect()
    {
        Console.WriteLine("===== ENCRYPTED SELECT DEBUG =====");
        Console.WriteLine();
        
        var dbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_encrypted_debug_{Guid.NewGuid()}");
        Directory.CreateDirectory(dbPath);
        
        try
        {
            // ✅ ENCRYPTED PageBased config (same as benchmark)
            var config = new DatabaseConfig
            {
                NoEncryptMode = false,  // ✅ ENCRYPTED!
                StorageEngineType = StorageEngineType.PageBased,
                EnablePageCache = true,
                PageCacheCapacity = 10000,
                UseGroupCommitWal = true,
                EnableAdaptiveWalBatching = true,
                HighSpeedInsertMode = true,
                UseOptimizedInsertPath = true,
                WorkloadHint = WorkloadHint.General,
                SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
                StrictParameterValidation = false
            };
            
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            
            Console.WriteLine("📦 Creating encrypted PageBased database...");
            using var db = (Database)factory.Create(dbPath, "password", false, config);
            
            // Create table
            Console.WriteLine("📋 Creating table...");
            db.ExecuteSQL(@"CREATE TABLE bench_records (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                salary DECIMAL,
                created DATETIME
            )");
            
            // Insert test data
            Console.WriteLine($"📝 Inserting {RecordCount} records...");
            var rows = new List<Dictionary<string, object>>(RecordCount);
            for (int i = 0; i < RecordCount; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["id"] = i,
                    ["name"] = $"User{i}",
                    ["email"] = $"user{i}@test.com",
                    ["age"] = 20 + (i % 50),
                    ["salary"] = (decimal)(30000 + (i % 70000)),
                    ["created"] = DateTime.Parse("2025-01-01")
                });
            }
            db.BulkInsertAsync("bench_records", rows).GetAwaiter().GetResult();
            Console.WriteLine($"✅ Inserted {RecordCount} records");
            
            // ✅ NOW TRY THE SELECT THAT CRASHES IN BENCHMARK
            Console.WriteLine();
            Console.WriteLine("🔍 Testing SELECT with WHERE (this crashes in benchmark)...");
            
            try
            {
                var sw = Stopwatch.StartNew();
                var results = db.ExecuteQuery("SELECT * FROM bench_records WHERE age > 30");
                sw.Stop();
                
                Console.WriteLine($"✅ SELECT succeeded!");
                Console.WriteLine($"   Rows returned: {results.Count}");
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds} ms");
                
                // Verify data
                if (results.Count > 0)
                {
                    var first = results[0];
                    Console.WriteLine($"   First row: id={first["id"]}, name={first["name"]}, age={first["age"]}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SELECT FAILED!");
                Console.WriteLine($"   Exception: {ex.GetType().Name}");
                Console.WriteLine($"   Message: {ex.Message}");
                Console.WriteLine($"   Stack trace:");
                Console.WriteLine(ex.StackTrace);
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}");
                }
            }
        }
        finally
        {
            try { Directory.Delete(dbPath, true); } catch { }
        }
    }
    
    static void ProfilePageBasedMode()
    {
        Console.WriteLine("===== PAGE_BASED MODE PROFILING =====");
        Console.WriteLine();
        
        var dbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_profile_pagebased_{Guid.NewGuid()}");
        Directory.CreateDirectory(dbPath);
        
        try
        {
            var config = new DatabaseConfig
            {
                NoEncryptMode = true,
                StorageEngineType = StorageEngineType.PageBased,
                EnablePageCache = true,
                PageCacheCapacity = 10000,
                UseGroupCommitWal = true,
                GroupCommitSize = 1000,
                WalBufferSize = 8 * 1024 * 1024,
                HighSpeedInsertMode = true,
                UseOptimizedInsertPath = true,
                WorkloadHint = WorkloadHint.WriteHeavy
            };
            
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            
            using var db = (Database)factory.Create(dbPath, "password", false, config);
            
            // Create table
            db.ExecuteSQL(@"CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                created_at DATETIME,
                is_active INTEGER
            )");
            
            Console.WriteLine("? Starting warmup runs...");
            for (int warmup = 0; warmup < WarmupRuns; warmup++)
            {
                var warmupRows = GenerateTestRows(100, warmup * 100);
                db.BulkInsertAsync("users", warmupRows).GetAwaiter().GetResult();
                Console.WriteLine($"  Warmup {warmup + 1}/{WarmupRuns} complete");
            }
            
            Console.WriteLine();
            Console.WriteLine("? Starting PROFILED INSERT run...");
            Console.WriteLine($"  Inserting {RecordCount:N0} records...");
            Console.WriteLine();
            
            var sw = Stopwatch.StartNew();
            
            // CRITICAL: This is the hot path we're profiling!
            var rows = GenerateTestRows(RecordCount, WarmupRuns * 100);
            db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
            
            sw.Stop();
            
            Console.WriteLine($"? PAGE_BASED INSERT: {sw.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Throughput: {RecordCount / (sw.ElapsedMilliseconds / 1000.0):N0} records/sec");
            Console.WriteLine();
            
            // ✅ FIX: Print available database statistics instead
            PrintDatabaseStatistics(db);
        }
        finally
        {
            try { Directory.Delete(dbPath, true); } catch { }
        }
    }
    
    static void ProfileColumnarMode()
    {
        Console.WriteLine("===== COLUMNAR MODE PROFILING =====");
        Console.WriteLine();
        
        var dbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_profile_columnar_{Guid.NewGuid()}");
        Directory.CreateDirectory(dbPath);
        
        try
        {
            var config = new DatabaseConfig
            {
                NoEncryptMode = true,
                StorageEngineType = StorageEngineType.AppendOnly,
                EnablePageCache = true,
                PageCacheCapacity = 10000,
                UseGroupCommitWal = true,
                GroupCommitSize = 1000,
                WalBufferSize = 8 * 1024 * 1024,
                HighSpeedInsertMode = true,
                UseOptimizedInsertPath = true,
                WorkloadHint = WorkloadHint.Analytics
            };
            
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            
            using var db = (Database)factory.Create(dbPath, "password", false, config);
            
            // Create table
            db.ExecuteSQL(@"CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                created_at DATETIME,
                is_active INTEGER
            )");
            
            Console.WriteLine("? Starting warmup runs...");
            for (int warmup = 0; warmup < WarmupRuns; warmup++)
            {
                var warmupRows = GenerateTestRows(100, warmup * 100);
                db.BulkInsertAsync("users", warmupRows).GetAwaiter().GetResult();
                Console.WriteLine($"  Warmup {warmup + 1}/{WarmupRuns} complete");
            }
            
            Console.WriteLine();
            Console.WriteLine("? Starting PROFILED INSERT run...");
            Console.WriteLine($"  Inserting {RecordCount:N0} records...");
            Console.WriteLine();
            
            var sw = Stopwatch.StartNew();
            
            // CRITICAL: This is the hot path we're profiling!
            var rows = GenerateTestRows(RecordCount, WarmupRuns * 100);
            db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
            
            sw.Stop();
            
            Console.WriteLine($"? COLUMNAR INSERT: {sw.ElapsedMilliseconds:N0} ms");
            Console.WriteLine($"  Throughput: {RecordCount / (sw.ElapsedMilliseconds / 1000.0):N0} records/sec");
            Console.WriteLine();
            
            // ✅ FIX: Print available database statistics instead
            PrintDatabaseStatistics(db);
        }
        finally
        {
            try { Directory.Delete(dbPath, true); } catch { }
        }
    }
    
    static void ContinuousProfiling()
    {
        Console.WriteLine("===== CONTINUOUS PROFILING MODE =====");
        Console.WriteLine();
        Console.WriteLine("? Running continuous inserts for profiling...");
        Console.WriteLine("  Press Ctrl+C to stop");
        Console.WriteLine();
        
        var dbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_profile_continuous_{Guid.NewGuid()}");
        Directory.CreateDirectory(dbPath);
        
        try
        {
            var config = new DatabaseConfig
            {
                NoEncryptMode = true,
                StorageEngineType = StorageEngineType.PageBased,
                EnablePageCache = true,
                PageCacheCapacity = 10000,
                UseGroupCommitWal = true,
                GroupCommitSize = 1000,
                WalBufferSize = 8 * 1024 * 1024,
                HighSpeedInsertMode = true,
                UseOptimizedInsertPath = true,
                WorkloadHint = WorkloadHint.WriteHeavy
            };
            
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            
            using var db = (Database)factory.Create(dbPath, "password", false, config);
            
            // Create table
            db.ExecuteSQL(@"CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                created_at DATETIME,
                is_active INTEGER
            )");
            
            int iteration = 0;
            var totalSw = Stopwatch.StartNew();
            
            while (true)
            {
                var rows = GenerateTestRows(1000, iteration * 1000);
                var sw = Stopwatch.StartNew();
                db.BulkInsertAsync("users", rows).GetAwaiter().GetResult();
                sw.Stop();
                
                iteration++;
                
                if (iteration % 10 == 0)
                {
                    Console.WriteLine($"? Iteration {iteration}: {sw.ElapsedMilliseconds:N0} ms (1000 records)");
                    Console.WriteLine($"  Avg throughput: {(iteration * 1000) / (totalSw.ElapsedMilliseconds / 1000.0):N0} records/sec");
                }
                
                Thread.Sleep(100); // Small delay between iterations
            }
        }
        finally
        {
            try { Directory.Delete(dbPath, true); } catch { }
        }
    }
    
    static List<Dictionary<string, object>> GenerateTestRows(int count, int startId)
    {
        var rows = new List<Dictionary<string, object>>(count);
        
        for (int i = 0; i < count; i++)
        {
            int id = startId + i;
            rows.Add(new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = $"User_{id}",
                ["email"] = $"user{id}@example.com",
                ["age"] = 20 + (i % 50),
                ["created_at"] = DateTime.UtcNow,  // ✅ FIX: Use DateTime object instead of string
                ["is_active"] = i % 2 == 0 ? 1 : 0
            });
        }
        
        return rows;
    }
    
    /// <summary>
    /// ✅ NEW: Print database statistics (cache stats, table info, etc.)
    /// </summary>
    static void PrintDatabaseStatistics(Database db)
    {
        Console.WriteLine("? Database Statistics:");
        
        try
        {
            var stats = db.GetDatabaseStatistics();
            
            // Core stats
            Console.WriteLine($"  Tables Count: {stats.GetValueOrDefault("TablesCount", 0)}");
            Console.WriteLine($"  Read-Only Mode: {stats.GetValueOrDefault("IsReadOnly", false)}");
            Console.WriteLine($"  No Encrypt Mode: {stats.GetValueOrDefault("NoEncryptMode", false)}");
            
            // Cache stats
            if (stats.TryGetValue("QueryCacheEnabled", out var qcEnabled) && (bool)qcEnabled)
            {
                Console.WriteLine($"  Query Cache Hits: {stats.GetValueOrDefault("QueryCacheHits", 0L)}");
                Console.WriteLine($"  Query Cache Misses: {stats.GetValueOrDefault("QueryCacheMisses", 0L)}");
                Console.WriteLine($"  Query Cache Hit Rate: {stats.GetValueOrDefault("QueryCacheHitRate", 0.0):P2}");
            }
            
            // ✅ UPDATED: Check for BOTH Core.Cache.PageCache AND PageManager.LruPageCache
            if (stats.TryGetValue("PageCacheEnabled", out var pcEnabled) && (bool)pcEnabled)
            {
                var hits = Convert.ToInt64(stats.GetValueOrDefault("PageCacheHits", 0L));
                var misses = Convert.ToInt64(stats.GetValueOrDefault("PageCacheMisses", 0L));
                
                // If Core.Cache.PageCache shows 0, try to get PageManager cache stats directly
                if (hits == 0 && misses == 0)
                {
                    Console.WriteLine("  ⚠️  Core.Cache.PageCache not used (0 hits/misses)");
                    Console.WriteLine("  ℹ️  PageBasedEngine uses internal LruPageCache");
                    Console.WriteLine("     (Stats collection not yet implemented for PageManager cache)");
                }
                else
                {
                    Console.WriteLine($"  Page Cache Hits: {hits}");
                    Console.WriteLine($"  Page Cache Misses: {misses}");
                    Console.WriteLine($"  Page Cache Hit Rate: {stats.GetValueOrDefault("PageCacheHitRate", 0.0):P2}");
                    Console.WriteLine($"  Page Cache Evictions: {stats.GetValueOrDefault("PageCacheEvictions", 0L)}");
                    Console.WriteLine($"  Page Cache Size: {stats.GetValueOrDefault("PageCacheSize", 0)}/{stats.GetValueOrDefault("PageCacheCapacity", 0)} pages");
                }
            }
            else
            {
                Console.WriteLine("  ℹ️  Page Cache: Disabled in config");
            }
            
            // ✅ NEW: Try to get PageManager cache stats directly if available
            Console.WriteLine();
            Console.WriteLine("  📊 Note: For detailed PageManager cache stats, use:");
            Console.WriteLine("     pageManager.GetCacheStats() in PageBasedEngine");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error retrieving statistics: {ex.Message}");
        }
    }
}
