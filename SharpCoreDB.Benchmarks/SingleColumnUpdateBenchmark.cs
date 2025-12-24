// <copyright file="SingleColumnUpdateBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using SharpCoreDB;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

/// <summary>
/// 🔥 NEW: Single-column UPDATE benchmark to test SQL parser optimization.
/// This test focuses on PRIMARY KEY-based single-column updates which should use
/// the optimized UpdateBatch&lt;TId, TValue&gt; path (5-7x speedup expected).
/// </summary>
public static class SingleColumnUpdateBenchmark
{
    /// <summary>
    /// Main entry point for single-column UPDATE benchmark.
    /// Tests PRIMARY KEY optimization with single column updates.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  🔥 Single-Column UPDATE Benchmark - Direct Lookup Optimization Test       ║");
        Console.WriteLine("║  Target: 5K updates from ~740ms → 100-150ms (5-7x speedup)                 ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝\n");

        var tempPath = Path.Combine(Path.GetTempPath(), $"single_col_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        try
        {
            // Initialize database
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();

            Console.WriteLine("SETUP");
            Console.WriteLine(new string('─', 80));
            Console.WriteLine("Initializing database...");
            IDatabase db = factory.Create(tempPath, "TestPassword123!");

            Console.WriteLine("Creating test table with PRIMARY KEY...");
            db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, price DECIMAL)");

            // Insert 10,000 initial records
            Console.WriteLine("Inserting 10,000 initial records...");
            var setupStopwatch = Stopwatch.StartNew();
            
            var insertStatements = new List<string>();
            for (int i = 1; i <= 10000; i++)
            {
                decimal price = 50000m + (i % 20000) * 0.5m;
                insertStatements.Add($"INSERT INTO products VALUES ({i}, 'Product{i}', {price.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
            }
            db.ExecuteBatchSQL(insertStatements);
            setupStopwatch.Stop();
            
            Console.WriteLine($"✓ Setup complete in {setupStopwatch.ElapsedMilliseconds}ms\n");

            // TEST: Single-column batch update with BeginBatchUpdate/EndBatchUpdate
            Console.WriteLine(new string('═', 80));
            Console.WriteLine("TEST: Batch UPDATE with Single Column (price) - PRIMARY KEY Optimization");
            Console.WriteLine(new string('═', 80));
            Console.WriteLine("Performing 5,000 random single-column updates...\n");

            var random = new Random(42);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                db.BeginBatchUpdate();

                for (int i = 0; i < 5000; i++)
                {
                    int productId = random.Next(1, 10001);
                    decimal newPrice = 50000m + (random.Next() % 50000) * 0.01m;
                    
                    // ✅ SINGLE COLUMN UPDATE - should trigger optimized path!
                    db.ExecuteSQL($"UPDATE products SET price = {newPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)} WHERE id = {productId}");

                    if ((i + 1) % 1000 == 0)
                    {
                        Console.WriteLine($"  Progress: {i + 1}/5000 updates ({((i + 1) * 100) / 5000}%)");
                    }
                }

                Console.WriteLine($"  Committing batch...");
                db.EndBatchUpdate();
            }
            catch
            {
                db.CancelBatchUpdate();
                throw;
            }

            stopwatch.Stop();
            var testTime = stopwatch.ElapsedMilliseconds;

            Console.WriteLine($"\n✓ Test Results:");
            Console.WriteLine($"  Time: {testTime}ms");
            Console.WriteLine($"  Per-update: {(double)testTime / 5000:F3}ms");
            Console.WriteLine($"  Throughput: {5000.0 / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");

            // Analysis
            Console.WriteLine("\n" + new string('═', 80));
            Console.WriteLine("PERFORMANCE ANALYSIS");
            Console.WriteLine(new string('═', 80));

            Console.WriteLine("\nExpected vs Actual:");
            Console.WriteLine($"  Baseline (no optimization):  ~740ms");
            Console.WriteLine($"  Expected (with optimization): 100-150ms (5-7x faster)");
            Console.WriteLine($"  Actual Result:               {testTime}ms");

            if (testTime < 200)
            {
                var speedup = 740.0 / testTime;
                Console.WriteLine($"\n✅ OPTIMIZATION ACTIVE! Speedup: {speedup:F2}x");
                Console.WriteLine($"   Direct position lookup is working correctly!");
            }
            else if (testTime < 400)
            {
                Console.WriteLine($"\n🟡 PARTIAL OPTIMIZATION - Better than baseline but not optimal");
                Console.WriteLine($"   May be using bulk SELECT instead of direct lookup");
            }
            else
            {
                Console.WriteLine($"\n⚠️  OPTIMIZATION NOT ACTIVE - Still using old code path");
                Console.WriteLine($"   Check SQL parser integration");
            }

            Console.WriteLine("\n" + new string('═', 80));
            Console.WriteLine("DIAGNOSTICS");
            Console.WriteLine(new string('═', 80));
            Console.WriteLine("\nSQL Statement Pattern:");
            Console.WriteLine($"  UPDATE products SET price = <value> WHERE id = <pk>");
            Console.WriteLine($"\nExpected Flow:");
            Console.WriteLine($"  1. SqlParser.ExecuteUpdate() detects PRIMARY KEY update");
            Console.WriteLine($"  2. TryOptimizedPrimaryKeyUpdate() checks conditions:");
            Console.WriteLine($"     - ✅ Single column update (price)");
            Console.WriteLine($"     - ✅ WHERE clause: id = <value>");
            Console.WriteLine($"     - ✅ id is PRIMARY KEY");
            Console.WriteLine($"  3. Route to Table.UpdateBatch<int, decimal>(\"id\", \"price\", updates)");
            Console.WriteLine($"  4. Direct index lookup (O(1)) instead of SELECT");
            Console.WriteLine($"\nTo see detailed diagnostics, run in DEBUG mode:");
            Console.WriteLine($"  dotnet run --project SharpCoreDB.Benchmarks SingleColumnUpdateBenchmark -c Debug");

            Console.WriteLine("\n✅ Benchmark completed!");
            
            ((IDisposable)db).Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Benchmark failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
