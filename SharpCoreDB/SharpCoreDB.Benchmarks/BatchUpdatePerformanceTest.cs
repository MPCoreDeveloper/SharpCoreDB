// <copyright file="BatchUpdatePerformanceTest.cs" company="MPCoreDeveloper">
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
/// Performance comparison test for batch UPDATE optimization.
/// 
/// TARGET: 5k random updates from 2,172ms to less than 400ms (5-10x speedup)
/// 
/// Design:
/// - Test 1: Standard UPDATE (baseline): 5k individual updates
/// - Test 2: Batch UPDATE: 5k updates in batch transaction
/// 
/// Example Usage:
///   dotnet run --project SharpCoreDB.Benchmarks -c Release
/// </summary>
public static class BatchUpdatePerformanceTest
{
    /// <summary>
    /// Main entry point to run batch update performance tests.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("🚀 SharpCoreDB Batch UPDATE Optimization Performance Test");
        Console.WriteLine("Target: 5k random updates from 2,172ms to <400ms (5-10x speedup)\n");

        var tempPath = Path.Combine(Path.GetTempPath(), $"batch_update_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        try
        {
            // Initialize database
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            
            IDatabase db = factory.Create(tempPath, "TestPassword123!");

            // Create test table
            Console.WriteLine("Creating test table...");
            db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, salary DECIMAL)");

            // Insert 5000 initial rows
            Console.WriteLine("Inserting 5,000 initial rows...");
            var insertStopwatch = Stopwatch.StartNew();
            
            var insertStatements = new List<string>();
            for (int i = 1; i <= 5000; i++)
            {
                insertStatements.Add($"INSERT INTO users VALUES ({i}, 'User{i}', {50000 + (i % 1000)})");
            }
            db.ExecuteBatchSQL(insertStatements);
            
            insertStopwatch.Stop();
            Console.WriteLine($"✓ Inserted 5,000 rows in {insertStopwatch.ElapsedMilliseconds}ms\n");

            // Test 1: Standard UPDATE (baseline)
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("TEST 1: Standard UPDATE (5,000 individual updates - BASELINE)");
            Console.WriteLine(new string('=', 70));

            var random = new Random(42);
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 5000; i++)
            {
                int id = random.Next(1, 5001);
                decimal newSalary = 50000 + (random.Next() % 20000);
                db.ExecuteSQL($"UPDATE users SET salary = {newSalary} WHERE id = {id}");
            }

            stopwatch.Stop();
            Console.WriteLine($"\n✓ Completed 5,000 standard updates in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"  - Per-update overhead: {stopwatch.Elapsed.TotalMilliseconds / 5000:F3}ms");
            Console.WriteLine($"  - Throughput: {5000 / stopwatch.Elapsed.TotalSeconds:F0} updates/sec");

            // Reset database
            Console.WriteLine("\nResetting database for batch test...");
            db.ExecuteSQL("DELETE FROM users");
            for (int i = 1; i <= 5000; i++)
            {
                db.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}', {50000 + (i % 1000)})");
            }

            // Test 2: Batch UPDATE (optimized)
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("TEST 2: Batch UPDATE (5,000 updates in batch transaction)");
            Console.WriteLine(new string('=', 70));

            random = new Random(42);
            stopwatch = Stopwatch.StartNew();

            try
            {
                // ✅ CRITICAL: Start batch transaction
                db.BeginBatchUpdate();

                for (int i = 0; i < 5000; i++)
                {
                    int id = random.Next(1, 5001);
                    decimal newSalary = 50000 + (random.Next() % 20000);
                    db.ExecuteSQL($"UPDATE users SET salary = {newSalary} WHERE id = {id}");
                }

                // ✅ CRITICAL: End batch transaction (single WAL flush + bulk index rebuild)
                db.EndBatchUpdate();
            }
            catch
            {
                db.CancelBatchUpdate();
                throw;
            }

            stopwatch.Stop();
            Console.WriteLine($"\n✓ Completed 5,000 batch updates in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"  - Per-update overhead: {stopwatch.Elapsed.TotalMilliseconds / 5000:F3}ms");
            Console.WriteLine($"  - Throughput: {5000 / stopwatch.Elapsed.TotalSeconds:F0} updates/sec");

            // Summary
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("SUMMARY");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("\nExpected Performance:");
            Console.WriteLine("  - 5k standard updates:  ~2,000ms (baseline - high per-update overhead)");
            Console.WriteLine("  - 5k batch updates:     <400ms (5-10x faster - deferred indexes)");
            Console.WriteLine("  - 10k batch updates:    ~700-800ms (linear scaling)");
            Console.WriteLine("  - 20k batch updates:    ~1,400-1,600ms (linear scaling)");
            Console.WriteLine("\nLiteDB Comparison (from README):");
            Console.WriteLine("  - LiteDB 5k updates:    ~407ms");
            Console.WriteLine("  - SharpCoreDB target:   <400ms ✅ BEAT LiteDB");
            
            Console.WriteLine("\n✅ Test completed successfully!");
            
            ((IDisposable)db).Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }
}
