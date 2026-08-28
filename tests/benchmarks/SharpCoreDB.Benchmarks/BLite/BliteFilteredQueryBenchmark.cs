// <copyright file="BliteFilteredQueryBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks.BLite;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Scenario B3: Filtered Query (1M documents, 10K queries).
/// Measures query performance with various WHERE clauses and filter complexity.
/// </summary>
public class BliteFilteredQueryBenchmark : BenchmarkContext
{
    private Database? _db;
    private string _dbPath = string.Empty;
    private const int TotalDocuments = 1_000_000;
    private const int TotalQueries = 10_000;
    private const string TableName = "documents";
    
    private readonly List<double> _simpleFilterLatencies = [];
    private readonly List<double> _rangeFilterLatencies = [];
    private readonly List<double> _multiFilterLatencies = [];
    private readonly List<double> _likeFilterLatencies = [];

    public override void Setup()
    {
        base.Setup();
        ScenarioName = "BLite B3: Filtered Query (1M documents)";
        Console.WriteLine($"[B3] Setup: {ScenarioName}");

        // Create database
        _dbPath = Path.Combine(Path.GetTempPath(), $"blite-b3-{Guid.NewGuid()}");
        
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

        // Bulk load 1M documents
        Console.WriteLine($"[B3] Loading {TotalDocuments:N0} documents...");
        var loadStart = Stopwatch.StartNew();
        
        var batchSize = 10_000;
        for (int i = 0; i < TotalDocuments; i += batchSize)
        {
            var batch = DataGenerator.GenerateBatch(batchSize, i + 1);
            var statements = batch.Select(doc => doc.ToInsertSQL(TableName)).ToList();
            _db.ExecuteBatchSQL(statements);
            
            if ((i + batchSize) % 100_000 == 0)
            {
                Console.WriteLine($"[B3] Loaded {i + batchSize:N0}/{TotalDocuments:N0} documents...");
            }
        }
        
        _db.Flush();
        loadStart.Stop();
        
        Console.WriteLine($"[B3] Data load complete in {loadStart.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"[B3] Database ready with {TotalDocuments:N0} documents");
    }

    public async Task Run()
    {
        Console.WriteLine($"[B3] Running: {ScenarioName}");
        Console.WriteLine();

        if (_db == null)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        // Query Type 1: Simple equality filter
        Console.WriteLine("[B3] Query Type 1: Simple Equality (age = X)");
        await RunSimpleFilterQueries();
        Console.WriteLine($"[B3] Complete: p50={CalculatePercentile(_simpleFilterLatencies, 50):F3}ms, p99={CalculatePercentile(_simpleFilterLatencies, 99):F3}ms");
        Console.WriteLine();

        // Query Type 2: Range filter
        Console.WriteLine("[B3] Query Type 2: Range Filter (age > X AND age < Y)");
        await RunRangeFilterQueries();
        Console.WriteLine($"[B3] Complete: p50={CalculatePercentile(_rangeFilterLatencies, 50):F3}ms, p99={CalculatePercentile(_rangeFilterLatencies, 99):F3}ms");
        Console.WriteLine();

        // Query Type 3: Multiple conditions
        Console.WriteLine("[B3] Query Type 3: Multiple Conditions (age + score + is_active)");
        await RunMultiFilterQueries();
        Console.WriteLine($"[B3] Complete: p50={CalculatePercentile(_multiFilterLatencies, 50):F3}ms, p99={CalculatePercentile(_multiFilterLatencies, 99):F3}ms");
        Console.WriteLine();

        // Query Type 4: LIKE pattern matching
        Console.WriteLine("[B3] Query Type 4: LIKE Pattern Matching");
        await RunLikeFilterQueries();
        Console.WriteLine($"[B3] Complete: p50={CalculatePercentile(_likeFilterLatencies, 50):F3}ms, p99={CalculatePercentile(_likeFilterLatencies, 99):F3}ms");
        Console.WriteLine();

        // Summary
        PrintSummary();
    }

    private async Task RunSimpleFilterQueries()
    {
        var random = new Random(42);
        // NOSONAR:S2245 - fixed-seed Random for reproducible benchmark data (non-security).
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalQueries; i++)
        {
            var age = random.Next(18, 101);
            
            var opStart = Stopwatch.GetTimestamp();
            _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE age = {age}");
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            _simpleFilterLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[B3] Executed {i + 1:N0}/{TotalQueries:N0} queries...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B3] Simple filter queries completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalQueries / sw.Elapsed.TotalSeconds:F0} QPS)");
    }

    private async Task RunRangeFilterQueries()
    {
        var random = new Random(42);
        // NOSONAR:S2245 - fixed-seed Random for reproducible benchmark data (non-security).
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalQueries; i++)
        {
            var minAge = random.Next(20, 60);
            var maxAge = minAge + random.Next(10, 40);
            
            var opStart = Stopwatch.GetTimestamp();
            _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE age > {minAge} AND age < {maxAge}");
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            _rangeFilterLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[B3] Executed {i + 1:N0}/{TotalQueries:N0} queries...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B3] Range filter queries completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalQueries / sw.Elapsed.TotalSeconds:F0} QPS)");
    }

    private async Task RunMultiFilterQueries()
    {
        var random = new Random(42);
        // NOSONAR:S2245 - fixed-seed Random for reproducible benchmark data (non-security).
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalQueries; i++)
        {
            var minAge = random.Next(25, 50);
            var maxAge = minAge + 25;
            var minScore = random.Next(0, 50);
            var isActive = random.Next(0, 2);
            
            var opStart = Stopwatch.GetTimestamp();
            _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE age > {minAge} AND age < {maxAge} AND score > {minScore} AND is_active = {isActive}");
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            _multiFilterLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[B3] Executed {i + 1:N0}/{TotalQueries:N0} queries...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B3] Multi-filter queries completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalQueries / sw.Elapsed.TotalSeconds:F0} QPS)");
    }

    private async Task RunLikeFilterQueries()
    {
        var patterns = new[] { "John%", "%Smith", "%son%", "M%a" };
        var random = new Random(42);
        // NOSONAR:S2245 - fixed-seed Random for reproducible benchmark data (non-security).
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < TotalQueries; i++)
        {
            var pattern = patterns[random.Next(patterns.Length)];
            
            var opStart = Stopwatch.GetTimestamp();
            _db.ExecuteSQL($"SELECT * FROM {TableName} WHERE name LIKE '{pattern}'");
            var elapsed = (Stopwatch.GetTimestamp() - opStart) * 1000.0 / Stopwatch.Frequency;
            
            _likeFilterLatencies.Add(elapsed);

            if ((i + 1) % 1_000 == 0)
            {
                Console.WriteLine($"[B3] Executed {i + 1:N0}/{TotalQueries:N0} queries...");
            }

            await Task.Yield();
        }

        sw.Stop();
        Console.WriteLine($"[B3] LIKE filter queries completed in {sw.Elapsed.TotalSeconds:F2}s ({TotalQueries / sw.Elapsed.TotalSeconds:F0} QPS)");
    }

    private void PrintSummary()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("[B3] BENCHMARK SUMMARY");
        Console.WriteLine("========================================");
        Console.WriteLine($"Total Documents: {TotalDocuments:N0}");
        Console.WriteLine($"Total Queries: {TotalQueries * 4:N0} ({TotalQueries:N0} per query type)");
        Console.WriteLine();

        PrintQueryTypeSummary("Simple Equality (age = X)", _simpleFilterLatencies);
        PrintQueryTypeSummary("Range Filter (age > X AND age < Y)", _rangeFilterLatencies);
        PrintQueryTypeSummary("Multiple Conditions", _multiFilterLatencies);
        PrintQueryTypeSummary("LIKE Pattern Matching", _likeFilterLatencies);

        Console.WriteLine("========================================");
    }

    private void PrintQueryTypeSummary(string queryType, List<double> latencies)
    {
        if (latencies.Count == 0) return;

        Console.WriteLine($"{queryType}:");
        Console.WriteLine($"  Queries: {latencies.Count:N0}");
        Console.WriteLine($"  QPS: {latencies.Count / (latencies.Sum() / 1000):F0}");
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
        Console.WriteLine($"[B3] Teardown: {ScenarioName}");
        
        if (_db != null)
        {
            try
            {
                _db.Dispose();
                
                if (Directory.Exists(_dbPath))
                {
                    Directory.Delete(_dbPath, recursive: true);
                    Console.WriteLine($"[B3] Cleaned up database: {_dbPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[B3] Warning: Failed to cleanup database: {ex.Message}");
            }
        }
    }
}
