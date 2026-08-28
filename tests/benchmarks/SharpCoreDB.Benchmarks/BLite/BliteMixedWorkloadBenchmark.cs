// <copyright file="BliteMixedWorkloadBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.BLite;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Scenario B4: Mixed Workload (10 minutes sustained load).
/// Simulates realistic application workload with mixed read/write operations.
/// </summary>
public class BliteMixedWorkloadBenchmark : BenchmarkContext
{
    private Database? _db;
    private string _dbPath = string.Empty;
    private const int DurationMinutes = 10;
    private const string TableName = "documents";
    private const int InitialDocuments = 100_000;
    
    private readonly List<double> _insertLatencies = [];
    private readonly List<double> _readLatencies = [];
    private readonly List<double> _updateLatencies = [];
    private readonly List<double> _deleteLatencies = [];
    private readonly List<double> _queryLatencies = [];
    
    private int _currentMaxId = 0;
    private readonly Random _random = new(42);

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "BLite B4: Mixed Workload (10 minutes)";
        Console.WriteLine($"[B4] Setup: {ScenarioName}");

        // Create database
        _dbPath = Path.Combine(Path.GetTempPath(), $"blite-b4-{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        
        _db = new Database(
            services: serviceProvider,
            dbPath: _dbPath,
            masterPassword: "benchmark-password-123",
            isReadOnly: false
        );

        // Create schema
        var createTableSQL = $@"
            CREATE TABLE {TableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                tags TEXT,
                created_at DATETIME,
                updated_at DATETIME,
                is_active INTEGER,
                metadata TEXT
            )";
        
        _db.ExecuteSQL(createTableSQL);

        // Load initial dataset
        Console.WriteLine($"[B4] Loading initial {InitialDocuments:N0} documents...");
        var loadStart = Stopwatch.StartNew();
        
        var batchSize = 10_000;
        for (int i = 0; i < InitialDocuments; i += batchSize)
        {
            var batch = DataGenerator.GenerateBatch(batchSize, i + 1);
            var statements = batch.Select(doc => doc.ToInsertSQL(TableName)).ToList();
            _db.ExecuteBatchSQL(statements);
        }
        
        _db.Flush();
        _currentMaxId = InitialDocuments;
        loadStart.Stop();
        
        Console.WriteLine($"[B4] Initial load complete in {loadStart.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"[B4] Database ready for mixed workload test");
    }

    public async Task Run()
    {
        Console.WriteLine($"[B4] Running: {ScenarioName}");
        Console.WriteLine($"[B4] Duration: {DurationMinutes} minutes");
        Console.WriteLine($"[B4] Workload mix: 50% reads, 30% inserts, 10% updates, 10% queries");
        Console.WriteLine();

        if (_db == null)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        var sw = Stopwatch.StartNew();
        var endTime = sw.Elapsed.Add(TimeSpan.FromMinutes(DurationMinutes));
        var operationCount = 0;
        var lastReportTime = sw.Elapsed;

        while (sw.Elapsed < endTime)
        {
            // Determine operation type (weighted random)
            var operationType = _random.Next(100);
            
            if (operationType < 50)
            {
                // 50% - Read by ID
                await ExecuteRead();
            }
            else if (operationType < 80)
            {
                // 30% - Insert
                await ExecuteInsert();
            }
            else if (operationType < 90)
            {
                // 10% - Update
                await ExecuteUpdate();
            }
            else
            {
                // 10% - Query
                await ExecuteQuery();
            }

            operationCount++;

            // Progress report every 30 seconds
            if ((sw.Elapsed - lastReportTime).TotalSeconds >= 30)
            {
                var elapsed = sw.Elapsed.TotalMinutes;
                var opsPerSec = operationCount / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"[B4] Progress: {elapsed:F1}/{DurationMinutes} min, {operationCount:N0} ops ({opsPerSec:F0} ops/sec), DB size: {_currentMaxId:N0} docs");
                lastReportTime = sw.Elapsed;
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B4] Mixed workload complete!");
        Console.WriteLine($"[B4] Total operations: {operationCount:N0}");
        Console.WriteLine($"[B4] Average throughput: {operationCount / sw.Elapsed.TotalSeconds:F0} ops/sec");
        Console.WriteLine();

        // Summary
        PrintSummary(sw.Elapsed.TotalSeconds, operationCount);
    }

    private async Task ExecuteRead()
    {
        var id = _random.Next(1, _currentMaxId + 1);
        
        var opStart = Stopwatch.GetTimestamp();
        _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE id = {id}");
        var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
        
        _readLatencies.Add(elapsed);
        await Task.CompletedTask;
    }

    private async Task ExecuteInsert()
    {
        var doc = DataGenerator.GenerateDocument();
        
        var opStart = Stopwatch.GetTimestamp();
        _db.ExecuteSQL(doc.ToInsertSQL(TableName));
        var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
        
        _insertLatencies.Add(elapsed);
        _currentMaxId++;
        
        // Flush periodically
        if (_insertLatencies.Count % 100 == 0)
        {
            _db.Flush();
        }
        
        await Task.CompletedTask;
    }

    private async Task ExecuteUpdate()
    {
        var id = _random.Next(1, _currentMaxId + 1);
        var newScore = _random.NextDouble() * 100;
        
        var opStart = Stopwatch.GetTimestamp();
        _db.ExecuteSQL($"UPDATE {TableName} SET score = {newScore}, updated_at = '{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}' WHERE id = {id}");
        var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
        
        _updateLatencies.Add(elapsed);
        
        // Flush periodically
        if (_updateLatencies.Count % 50 == 0)
        {
            _db.Flush();
        }
        
        await Task.CompletedTask;
    }

    private async Task ExecuteQuery()
    {
        var minAge = _random.Next(20, 60);
        var maxAge = minAge + _random.Next(10, 40);
        
        var opStart = Stopwatch.GetTimestamp();
        _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE age > {minAge} AND age < {maxAge}");
        var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
        
        _queryLatencies.Add(elapsed);
        await Task.CompletedTask;
    }

    private void PrintSummary(double totalSeconds, int totalOps)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[B4] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Duration: {totalSeconds / 60:F2} minutes");
        Console.WriteLine($"Total Operations: {totalOps:N0}");
        Console.WriteLine($"Average Throughput: {totalOps / totalSeconds:F0} ops/sec");
        Console.WriteLine($"Final Database Size: {_currentMaxId:N0} documents");
        Console.WriteLine();

        PrintOperationTypeSummary("INSERT", _insertLatencies, totalOps);
        PrintOperationTypeSummary("READ (by ID)", _readLatencies, totalOps);
        PrintOperationTypeSummary("UPDATE", _updateLatencies, totalOps);
        PrintOperationTypeSummary("QUERY (range)", _queryLatencies, totalOps);

        Console.WriteLine("========================================");
    }

    private void PrintOperationTypeSummary(string opType, List<double> latencies, int totalOps)
    {
        if (latencies.Count == 0) return;

        var percentage = (latencies.Count / (double)totalOps) * 100;
        
        Console.WriteLine($"{opType}:");
        Console.WriteLine($"  Operations: {latencies.Count:N0} ({percentage:F1}% of total)");
        Console.WriteLine($"  Throughput: {latencies.Count / (latencies.Sum() / 1000):F0} ops/sec");
        Console.WriteLine($"  Latency p50: {CalculatePercentile(latencies, 50):F3} ms");
        Console.WriteLine($"  Latency p95: {CalculatePercentile(latencies, 95):F3} ms");
        Console.WriteLine($"  Latency p99: {CalculatePercentile(latencies, 99):F3} ms");
        Console.WriteLine($"  Latency max: {latencies.Max():F3} ms");
        Console.WriteLine();
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
        Console.WriteLine($"[B4] Teardown: {ScenarioName}");
        
        if (_db != null)
        {
            try
            {
                _db.Dispose();
                
                if (Directory.Exists(_dbPath))
                {
                    Directory.Delete(_dbPath, recursive: true);
                    Console.WriteLine($"[B4] Cleaned up database: {_dbPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[B4] Warning: Failed to cleanup database: {ex.Message}");
            }
        }
    }
}
