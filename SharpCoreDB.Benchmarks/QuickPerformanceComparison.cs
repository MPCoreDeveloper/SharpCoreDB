// <copyright file="QuickPerformanceComparison.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using System.Diagnostics;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Quick 10K record benchmark - SharpCoreDB vs SQLite vs LiteDB.
/// FAIR COMPARISON: Tests both batch transactions and individual inserts.
/// </summary>
public static class QuickPerformanceComparison
{
    private const int RecordCount = 10_000;

    public static void Run()
    {
        Console.Clear();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  SharpCoreDB vs SQLite vs LiteDB - 10K Records Benchmark");
        Console.WriteLine("  FAIR COMPARISON: Batch Transactions vs Individual Inserts");
        Console.WriteLine("═══════════════════════════════════════════════════════════\n");

        // Interactive menu
        while (true)
        {
            Console.WriteLine("Select benchmark to run:\n");
            Console.WriteLine("  1. Quick Test (SharpCoreDB Batch + SQLite) - 5 seconds ⚡");
            Console.WriteLine("  2. Fair Comparison (All databases, batch mode) - 10 seconds ✅ AANBEVOLEN");
            Console.WriteLine("  3. Full Suite (Batch + Individual inserts) - 30 seconds");
            Console.WriteLine("  4. SharpCoreDB Only (No Encryption)");
            Console.WriteLine("  5. SharpCoreDB Only (Encrypted)");
            Console.WriteLine("  6. SQLite Only");
            Console.WriteLine("  7. LiteDB Only");
            Console.WriteLine("  Q. Quit\n");

            Console.Write("Enter choice (1-7 or Q): ");
            var choice = Console.ReadLine()?.Trim().ToUpper();
            Console.WriteLine();

            if (choice == "Q")
            {
                Console.WriteLine("👋 Goodbye!\n");
                return;
            }

            try
            {
                RunBenchmark(choice ?? "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine($"   {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner: {ex.InnerException.Message}\n");
                }
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey();
            }

            Console.WriteLine("\n" + new string('═', 63));
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
            Console.Clear();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine("  SharpCoreDB vs SQLite vs LiteDB - 10K Records Benchmark");
            Console.WriteLine("  FAIR COMPARISON: Batch Transactions vs Individual Inserts");
            Console.WriteLine("═══════════════════════════════════════════════════════════\n");
        }
    }

    static void RunBenchmark(string choice)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Quick10k_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dataGenerator = new TestDataGenerator();
            var results = new List<(string Name, long TimeMs, string Notes)>();

            switch (choice)
            {
                case "1":
                    Console.WriteLine("🚀 Running Quick Test (Recommended for first-time users)...\n");
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: false);
                    RunSQLiteMemory(tempDir, dataGenerator, results);
                    break;

                case "2":
                    Console.WriteLine("🚀 Running Fair Comparison (All databases, batch mode)...\n");
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: false);
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: true);
                    RunSQLiteMemory(tempDir, dataGenerator, results);
                    RunSQLiteFile(tempDir, dataGenerator, results);
                    RunLiteDB(tempDir, dataGenerator, results);
                    break;

                case "3":
                    Console.WriteLine("🚀 Running Full Suite (All modes)...\n");
                    RunSharpCoreDBIndividual(tempDir, dataGenerator, results, encrypted: false);
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: false);
                    RunSharpCoreDBIndividual(tempDir, dataGenerator, results, encrypted: true);
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: true);
                    RunSQLiteMemory(tempDir, dataGenerator, results);
                    RunSQLiteFile(tempDir, dataGenerator, results);
                    RunLiteDB(tempDir, dataGenerator, results);
                    break;

                case "4":
                    Console.WriteLine("🚀 Running SharpCoreDB (No Encryption)...\n");
                    RunSharpCoreDBIndividual(tempDir, dataGenerator, results, encrypted: false);
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: false);
                    break;

                case "5":
                    Console.WriteLine("🚀 Running SharpCoreDB (Encrypted)...\n");
                    RunSharpCoreDBIndividual(tempDir, dataGenerator, results, encrypted: true);
                    RunSharpCoreDBBatch(tempDir, dataGenerator, results, encrypted: true);
                    break;

                case "6":
                    Console.WriteLine("🚀 Running SQLite Tests...\n");
                    RunSQLiteMemory(tempDir, dataGenerator, results);
                    RunSQLiteFile(tempDir, dataGenerator, results);
                    break;

                case "7":
                    Console.WriteLine("🚀 Running LiteDB Test...\n");
                    RunLiteDB(tempDir, dataGenerator, results);
                    break;

                default:
                    Console.WriteLine("❌ Invalid choice. Please try again.\n");
                    return;
            }

            // Display summary
            DisplaySummary(results);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    static void RunSharpCoreDBIndividual(string tempDir, TestDataGenerator dataGenerator, List<(string, long, string)> results, bool encrypted)
    {
        var encLabel = encrypted ? "Encrypted" : "No Encryption";
        Console.WriteLine($"🔹 Testing SharpCoreDB ({encLabel} - Individual Inserts)...");

        var dbPath = Path.Combine(tempDir, $"sharpcore_{(encrypted ? "enc" : "noenc")}_individual");
        using (var sharpDb = new BenchmarkDatabaseHelper(dbPath, enableEncryption: encrypted))
        {
            sharpDb.CreateUsersTable();

            var users = dataGenerator.GenerateUsers(RecordCount);

            var sw = Stopwatch.StartNew();
            foreach (var user in users)
            {
                sharpDb.InsertUserBenchmark(user.Id, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
            }
            sw.Stop();

            Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
            Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec");
            Console.WriteLine($"   ⚠️  WARNING: Individual transactions (unfair comparison)\n");

            results.Add(($"SharpCoreDB ({encLabel} - Individual)", sw.ElapsedMilliseconds, "Individual transactions"));
        }
    }

    static void RunSharpCoreDBBatch(string tempDir, TestDataGenerator dataGenerator, List<(string, long, string)> results, bool encrypted)
    {
        var encLabel = encrypted ? "Encrypted" : "No Encryption";
        Console.WriteLine($"🔹 Testing SharpCoreDB ({encLabel} - Batch Transaction)...");

        var dbPath = Path.Combine(tempDir, $"sharpcore_{(encrypted ? "enc" : "noenc")}_batch");
        using (var sharpDb = new BenchmarkDatabaseHelper(dbPath, enableEncryption: encrypted))
        {
            sharpDb.CreateUsersTable();

            var users = dataGenerator.GenerateUsers(RecordCount);
            var userList = users.Select(u => (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();

            var sw = Stopwatch.StartNew();
            sharpDb.InsertUsersTrueBatch(userList);
            sw.Stop();

            Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
            Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec");
            Console.WriteLine($"   ✅ FAIR: Single transaction with {userList.Count} batched inserts\n");

            results.Add(($"SharpCoreDB ({encLabel} - Batch)", sw.ElapsedMilliseconds, "Single transaction"));
        }
    }

    static void RunSQLiteMemory(string tempDir, TestDataGenerator dataGenerator, List<(string, long, string)> results)
    {
        Console.WriteLine("🔹 Testing SQLite (Memory)...");

        using (var sqliteConn = new SqliteConnection("Data Source=:memory:"))
        {
            sqliteConn.Open();

            using (var cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE Users (
                        Id INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Email TEXT NOT NULL,
                        Age INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        IsActive INTEGER NOT NULL
                    )";
                cmd.ExecuteNonQuery();
            }

            var users = dataGenerator.GenerateUsers(RecordCount);

            var sw = Stopwatch.StartNew();
            using (var transaction = sqliteConn.BeginTransaction())
            {
                using (var cmd = sqliteConn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Users (Id, Name, Email, Age, CreatedAt, IsActive) VALUES (@id, @name, @email, @age, @created, @active)";

                    foreach (var user in users)
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", user.Id);
                        cmd.Parameters.AddWithValue("@name", user.Name);
                        cmd.Parameters.AddWithValue("@email", user.Email);
                        cmd.Parameters.AddWithValue("@age", user.Age);
                        cmd.Parameters.AddWithValue("@created", user.CreatedAt.ToString("O"));
                        cmd.Parameters.AddWithValue("@active", user.IsActive ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
            sw.Stop();

            Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
            Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");

            results.Add(("SQLite (Memory)", sw.ElapsedMilliseconds, "Single transaction"));
        }
    }

    static void RunSQLiteFile(string tempDir, TestDataGenerator dataGenerator, List<(string, long, string)> results)
    {
        Console.WriteLine("🔹 Testing SQLite (File + WAL + FullSync)...");

        var dbPath = Path.Combine(tempDir, "sqlite_file.db");
        using (var sqliteConn = new SqliteConnection($"Data Source={dbPath}"))
        {
            sqliteConn.Open();

            using (var cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"
                    CREATE TABLE Users (
                        Id INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Email TEXT NOT NULL,
                        Age INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        IsActive INTEGER NOT NULL
                    )";
                cmd.ExecuteNonQuery();
            }

            var users = dataGenerator.GenerateUsers(RecordCount);

            var sw = Stopwatch.StartNew();
            using (var transaction = sqliteConn.BeginTransaction())
            {
                using (var cmd = sqliteConn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO Users (Id, Name, Email, Age, CreatedAt, IsActive) VALUES (@id, @name, @email, @age, @created, @active)";

                    foreach (var user in users)
                    {
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", user.Id);
                        cmd.Parameters.AddWithValue("@name", user.Name);
                        cmd.Parameters.AddWithValue("@email", user.Email);
                        cmd.Parameters.AddWithValue("@age", user.Age);
                        cmd.Parameters.AddWithValue("@created", user.CreatedAt.ToString("O"));
                        cmd.Parameters.AddWithValue("@active", user.IsActive ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
            sw.Stop();

            Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
            Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");

            results.Add(("SQLite (File+WAL+FullSync)", sw.ElapsedMilliseconds, "Single transaction"));
        }
    }

    static void RunLiteDB(string tempDir, TestDataGenerator dataGenerator, List<(string, long, string)> results)
    {
        Console.WriteLine("🔹 Testing LiteDB...");

        var dbPath = Path.Combine(tempDir, "litedb.db");
        using (var liteDb = new LiteDatabase($"Filename={dbPath};Connection=shared"))
        {
            var collection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");

            var users = dataGenerator.GenerateUsers(RecordCount);

            var sw = Stopwatch.StartNew();
            collection.InsertBulk(users);
            sw.Stop();

            Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
            Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");

            results.Add(("LiteDB", sw.ElapsedMilliseconds, "Bulk insert"));
        }
    }

    static void DisplaySummary(List<(string Name, long TimeMs, string Notes)> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        Console.WriteLine("\n" + new string('═', 63));
        Console.WriteLine("  📊 BENCHMARK RESULTS SUMMARY");
        Console.WriteLine(new string('═', 63) + "\n");

        // Find fastest for comparison
        var fastest = results.Min(r => r.TimeMs);

        Console.WriteLine($"{"Database",-45} {"Time",-10} {"vs Fastest",10}");
        Console.WriteLine(new string('-', 63));

        foreach (var result in results.OrderBy(r => r.TimeMs))
        {
            var ratio = result.TimeMs / (double)fastest;
            var comparison = ratio > 1 ? $"{ratio:F1}x slower" : "FASTEST";

            var icon = result.Notes.Contains("FAIR") || result.Notes.Contains("Single") ? "✅" : "⚠️";
            Console.WriteLine($"{icon} {result.Name,-43} {result.TimeMs,6}ms {comparison,12}");
        }

        Console.WriteLine(new string('═', 63));

        // Show batch vs individual comparison if both present
        var batchNoEnc = results.FirstOrDefault(r => r.Name.Contains("No Encryption - Batch"));
        var indivNoEnc = results.FirstOrDefault(r => r.Name.Contains("No Encryption - Individual"));

        if (batchNoEnc.Name != null && indivNoEnc.Name != null)
        {
            var improvement = indivNoEnc.TimeMs / (double)batchNoEnc.TimeMs;
            Console.WriteLine($"\n💡 KEY INSIGHT:");
            Console.WriteLine($"   Batch transactions are {improvement:F1}x faster than individual inserts");
            Console.WriteLine($"   ({indivNoEnc.TimeMs}ms → {batchNoEnc.TimeMs}ms)\n");
        }

        // Show encryption overhead if both present
        var batchEnc = results.FirstOrDefault(r => r.Name.Contains("Encrypted - Batch"));
        if (batchNoEnc.Name != null && batchEnc.Name != null)
        {
            var overhead = ((batchEnc.TimeMs - batchNoEnc.TimeMs) / (double)batchNoEnc.TimeMs) * 100;
            Console.WriteLine($"🔒 ENCRYPTION OVERHEAD:");
            Console.WriteLine($"   +{overhead:F1}% ({batchNoEnc.TimeMs}ms → {batchEnc.TimeMs}ms)\n");
        }
    }
}
