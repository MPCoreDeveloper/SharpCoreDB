// <copyright file="BliteCrudBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.BLite;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Scenario B1: Basic CRUD operations (100K operations).
/// Measures insert, read, update, delete throughput and latency.
/// </summary>
public class BliteCrudBenchmark : BenchmarkContext
{
    private Database? _db;
    private string _dbPath = string.Empty;
    private const int TotalInserts = 100_000;
    private const int TotalReads = 100_000;
    private const int TotalFiltered = 10_000;
    private const int TotalUpdates = 10_000;
    private const int TotalDeletes = 10_000;
    private const string TableName = "documents";
    private readonly List<double> _insertLatencies = [];
    private readonly List<double> _readLatencies = [];
    private readonly List<double> _filteredLatencies = [];
    private readonly List<double> _updateLatencies = [];
    private readonly List<double> _deleteLatencies = [];

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "BLite B1: Basic CRUD (100K operations)";
        Console.WriteLine($"[B1] Setup: {ScenarioName}");

        // Create database with SharpCoreDB
        _dbPath = Path.Combine(Path.GetTempPath(), $"blite-b1-{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        
        _db = new Database(
            services: serviceProvider,
            dbPath: _dbPath,
            masterPassword: "benchmark-password-123",
            isReadOnly: false
        );

        // Create schema matching specification
        var createTableSQL = $@"
            CREATE TABLE {TableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT UNIQUE,
                age INTEGER,
                score REAL,
                tags TEXT,
                created_at DATETIME,
                updated_at DATETIME,
                is_active INTEGER,
                metadata TEXT
            )";
        
        _db.ExecuteSQL(createTableSQL);
        Console.WriteLine($"[B1] Database created at: {_dbPath}");
        Console.WriteLine($"[B1] Schema: {TableName} table with 10 columns");
    }

    public async Task Run()
    {
        Console.WriteLine($"[B1] Running: {ScenarioName}");
        Console.WriteLine();

        if (_db == null)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        // Phase 1: INSERT (100K single inserts)
        Console.WriteLine("[B1] Phase 1: INSERT (100,000 operations)");
        await RunInsertPhase();
        Console.WriteLine($"[B1] Phase 1 Complete: {TotalInserts} inserts, p50={CalculatePercentile(_insertLatencies, 50):F3}ms, p99={CalculatePercentile(_insertLatencies, 99):F3}ms");
        Console.WriteLine();

        // Phase 2: SELECT by PK (100K random reads)
        Console.WriteLine("[B1] Phase 2: SELECT by Primary Key (100,000 operations)");
        await RunReadPhase();
        Console.WriteLine($"[B1] Phase 2 Complete: {TotalReads} reads, p50={CalculatePercentile(_readLatencies, 50):F3}ms, p99={CalculatePercentile(_readLatencies, 99):F3}ms");
        Console.WriteLine();

        // Phase 3: SELECT with filter (10K queries)
        Console.WriteLine("[B1] Phase 3: SELECT with WHERE filter (10,000 operations)");
        await RunFilteredQueryPhase();
        Console.WriteLine($"[B1] Phase 3 Complete: {TotalFiltered} filtered queries, p50={CalculatePercentile(_filteredLatencies, 50):F3}ms, p99={CalculatePercentile(_filteredLatencies, 99):F3}ms");
        Console.WriteLine();

        // Phase 4: UPDATE (10K random updates)
        Console.WriteLine("[B1] Phase 4: UPDATE (10,000 operations)");
        await RunUpdatePhase();
        Console.WriteLine($"[B1] Phase 4 Complete: {TotalUpdates} updates, p50={CalculatePercentile(_updateLatencies, 50):F3}ms, p99={CalculatePercentile(_updateLatencies, 99):F3}ms");
        Console.WriteLine();

        // Phase 5: DELETE (10K random deletes)
        Console.WriteLine("[B1] Phase 5: DELETE (10,000 operations)");
        await RunDeletePhase();
        Console.WriteLine($"[B1] Phase 5 Complete: {TotalDeletes} deletes, p50={CalculatePercentile(_deleteLatencies, 50):F3}ms, p99={CalculatePercentile(_deleteLatencies, 99):F3}ms");
        Console.WriteLine();

        // Phase 6: Full table scan
        Console.WriteLine("[B1] Phase 6: SELECT * (full table scan)");
        await RunFullScanPhase();
        Console.WriteLine();

        // Summary
        PrintSummary();
    }

    private async Task RunInsertPhase()
    {
        var sw = Stopwatch.StartNew();
        var insertStatements = new List<string>();

        for (int i = 0; i < TotalInserts; i++)
        {
            var doc = DataGenerator.GenerateDocument(i + 1); // Unique ID starting from 1
            var opStart = Stopwatch.GetTimestamp();
            
            insertStatements.Add(doc.ToInsertSQL(TableName));
            
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            _insertLatencies.Add(elapsed);

            if ((i + 1) % 10_000 == 0)
            {
                // Execute batch
                _db!.ExecuteBatchSQL(insertStatements);
                insertStatements.Clear();
                _db!.Flush();
                Console.WriteLine($"[B1] Inserted {i + 1:N0}/{TotalInserts:N0} documents...");
            }

            await Task.Yield(); // Cooperative yielding
        }

        // Execute remaining
        if (insertStatements.Count > 0)
        {
            _db!.ExecuteBatchSQL(insertStatements);
            _db!.Flush();
        }

        sw.Stop();
        Console.WriteLine($"[B1] Insert phase completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalInserts / sw.Elapsed.TotalSeconds:F0} docs/sec)");
    }

    private async Task RunReadPhase()
    {
        var random = new Random(42);
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalReads; i++)
        {
            var id = random.Next(1, TotalInserts + 1);
            var opStart = Stopwatch.GetTimestamp();
            
            _db!.ExecuteSQL($"SELECT * FROM {TableName} WHERE id = {id}");
            
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            _readLatencies.Add(elapsed);

            if ((i + 1) % 10_000 == 0)
            {
                Console.WriteLine($"[B1] Read {i + 1:N0}/{TotalReads:N0} documents...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B1] Read phase completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalReads / sw.Elapsed.TotalSeconds:F0} reads/sec)");
    }

    private async Task RunFilteredQueryPhase()
    {
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalFiltered; i++)
        {
            var opStart = Stopwatch.GetTimestamp();
            
            // Query: age > 25 AND age < 75
            _db!.ExecuteSQL($"SELECT * FROM {TableName} WHERE age > 25 AND age < 75");
            
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            _filteredLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[B1] Filtered query {i + 1:N0}/{TotalFiltered:N0}...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B1] Filtered query phase completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalFiltered / sw.Elapsed.TotalSeconds:F0} queries/sec)");
    }

    private async Task RunUpdatePhase()
    {
        var random = new Random(42);
        var sw = Stopwatch.StartNew();
        var updateStatements = new List<string>();

        for (int i = 0; i < TotalUpdates; i++)
        {
            var id = random.Next(1, TotalInserts + 1);
            var newScore = random.NextDouble() * 100;
            
            var opStart = Stopwatch.GetTimestamp();
            
            updateStatements.Add($"UPDATE {TableName} SET score = {newScore}, updated_at = '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}' WHERE id = {id}");
            
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            _updateLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                // Execute batch
                _db!.ExecuteBatchSQL(updateStatements);
                updateStatements.Clear();
                _db!.Flush();
                Console.WriteLine($"[B1] Updated {i + 1:N0}/{TotalUpdates:N0} documents...");
            }

            await Task.Yield();
        }

        // Execute remaining
        if (updateStatements.Count > 0)
        {
            _db!.ExecuteBatchSQL(updateStatements);
            _db!.Flush();
        }

        sw.Stop();
        Console.WriteLine($"[B1] Update phase completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalUpdates / sw.Elapsed.TotalSeconds:F0} updates/sec)");
    }

    private async Task RunDeletePhase()
    {
        var random = new Random(42);
        var sw = Stopwatch.StartNew();
        var deleteStatements = new List<string>();

        for (int i = 0; i < TotalDeletes; i++)
        {
            var id = random.Next(1, TotalInserts + 1);
            
            var opStart = Stopwatch.GetTimestamp();
            
            deleteStatements.Add($"DELETE FROM {TableName} WHERE id = {id}");
            
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            _deleteLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                // Execute batch
                _db!.ExecuteBatchSQL(deleteStatements);
                deleteStatements.Clear();
                _db!.Flush();
                Console.WriteLine($"[B1] Deleted {i + 1:N0}/{TotalDeletes:N0} documents...");
            }

            await Task.Yield();
        }

        // Execute remaining
        if (deleteStatements.Count > 0)
        {
            _db!.ExecuteBatchSQL(deleteStatements);
            _db!.Flush();
        }

        sw.Stop();
        Console.WriteLine($"[B1] Delete phase completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalDeletes / sw.Elapsed.TotalSeconds:F0} deletes/sec)");
    }

    private async Task RunFullScanPhase()
    {
        var sw = Stopwatch.StartNew();
        
        _db!.ExecuteSQL($"SELECT COUNT(*) FROM {TableName}");
        
        sw.Stop();
        Console.WriteLine($"[B1] Full scan completed in {sw.Elapsed.TotalMilliseconds:F2}ms");

        await Task.CompletedTask;
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[B1] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Total Operations: {TotalInserts + TotalReads + TotalFiltered + TotalUpdates + TotalDeletes:N0}");
        Console.WriteLine();
        Console.WriteLine("Phase 1 - INSERT:");
        Console.WriteLine($"  Operations: {TotalInserts:N0}");
        Console.WriteLine($"  Throughput: {TotalInserts / (_insertLatencies.Sum() / 1000):F0} ops/sec");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(_insertLatencies, 50):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(_insertLatencies, 99):F3} ms");
        Console.WriteLine($"  Latency max: {_insertLatencies.Max():F3} ms");
        Console.WriteLine();
        Console.WriteLine("Phase 2 - SELECT by PK:");
        Console.WriteLine($"  Operations: {TotalReads:N0}");
        Console.WriteLine($"  QPS: {TotalReads / (_readLatencies.Sum() / 1000):F0}");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(_readLatencies, 50):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(_readLatencies, 99):F3} ms");
        Console.WriteLine();
        Console.WriteLine("Phase 3 - SELECT with WHERE:");
        Console.WriteLine($"  Operations: {TotalFiltered:N0}");
        Console.WriteLine($"  QPS: {TotalFiltered / (_filteredLatencies.Sum() / 1000):F0}");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(_filteredLatencies, 50):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(_filteredLatencies, 99):F3} ms");
        Console.WriteLine();
        Console.WriteLine("Phase 4 - UPDATE:");
        Console.WriteLine($"  Operations: {TotalUpdates:N0}");
        Console.WriteLine($"  Throughput: {TotalUpdates / (_updateLatencies.Sum() / 1000):F0} ops/sec");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(_updateLatencies, 50):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(_updateLatencies, 99):F3} ms");
        Console.WriteLine();
        Console.WriteLine("Phase 5 - DELETE:");
        Console.WriteLine($"  Operations: {TotalDeletes:N0}");
        Console.WriteLine($"  Throughput: {TotalDeletes / (_deleteLatencies.Sum() / 1000):F0} ops/sec");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(_deleteLatencies, 50):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(_deleteLatencies, 99):F3} ms");
        Console.WriteLine("========================================");
    }

    private static double CalculatePercentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        
        var sorted = new List<double>(values);
        sorted.Sort();
        
        var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        
        return sorted[index];
    }

    public override void Teardown()
    {
        base.Teardown();
        Console.WriteLine($"[B1] Teardown: {ScenarioName}");
        
        if (_db != null)
        {
            try
            {
                _db.Dispose();
                
                if (Directory.Exists(_dbPath))
                {
                    Directory.Delete(_dbPath, recursive: true);
                    Console.WriteLine($"[B1] Cleaned up database: {_dbPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[B1] Warning: Failed to cleanup database: {ex.Message}");
            }
        }
    }
}
