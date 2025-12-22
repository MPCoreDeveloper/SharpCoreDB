// <copyright file="BTreeIndexRangeQueryBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks B-tree index performance for range queries vs full table scan.
/// Target: B-tree range query &lt; 10ms vs ~30ms full scan on 10k records.
/// 
/// Test Scenarios:
/// 1. Full Table Scan (baseline) - SELECT * WHERE age &gt; 30
/// 2. Hash Index Scan (control) - Falls back to full scan for range queries
/// 3. B-Tree Range Query - SELECT * WHERE age &gt; 30 using B-tree index
/// 4. B-Tree BETWEEN Query - SELECT * WHERE age BETWEEN 25 AND 35
/// 5. B-Tree ORDER BY - SELECT * FROM users ORDER BY age (uses B-tree sorted iteration)
/// 
/// Expected Results:
/// - Full scan: ~30ms (10k records, columnar storage)
/// - Hash index: ~30ms (no benefit for range queries)
/// - B-tree range: &lt; 10ms (3-5x faster with index)
/// - B-tree BETWEEN: &lt; 15ms (selective range)
/// - B-tree ORDER BY: &lt; 5ms (sorted iteration vs external sort)
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BTreeIndexRangeQueryBenchmark
{
    private BenchmarkDatabaseHelper? _dbFullScan;
    private BenchmarkDatabaseHelper? _dbHashIndex;
    private BenchmarkDatabaseHelper? _dbBTreeIndex;
    private const int RECORD_COUNT = 10_000;
    private const string DB_PATH_FULLSCAN = "./bench_btree_fullscan";
    private const string DB_PATH_HASH = "./bench_btree_hash";
    private const string DB_PATH_BTREE = "./bench_btree_btree";

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing test databases
        CleanupDatabase(DB_PATH_FULLSCAN);
        CleanupDatabase(DB_PATH_HASH);
        CleanupDatabase(DB_PATH_BTREE);

        // Setup 1: Full scan (no indexes)
        _dbFullScan = new BenchmarkDatabaseHelper(DB_PATH_FULLSCAN, enableEncryption: false);
        _dbFullScan.CreateUsersTableColumnar();
        InsertTestData(_dbFullScan);

        // Setup 2: Hash index (for comparison - should not help with range queries)
        _dbHashIndex = new BenchmarkDatabaseHelper(DB_PATH_HASH, enableEncryption: false);
        _dbHashIndex.CreateUsersTableColumnar();
        InsertTestData(_dbHashIndex);
        // Create hash index on age - won't help with range queries
        ((SharpCoreDB.Database)((dynamic)_dbHashIndex).database)
            .ExecuteSQL("CREATE INDEX idx_age_hash ON users (age)");

        // Setup 3: B-tree index (should significantly improve range queries)
        _dbBTreeIndex = new BenchmarkDatabaseHelper(DB_PATH_BTREE, enableEncryption: false);
        _dbBTreeIndex.CreateUsersTableColumnar();
        InsertTestData(_dbBTreeIndex);
        // Create B-tree index on age for range queries
        ((SharpCoreDB.Database)((dynamic)_dbBTreeIndex).database)
            .ExecuteSQL("CREATE INDEX idx_age_btree ON users (age) USING BTREE");
    }

    private void InsertTestData(BenchmarkDatabaseHelper db)
    {
        // Insert 10k records with ages distributed 18-65
        var users = new List<(int id, string name, string email, int age, DateTime createdAt, bool isActive)>();
        
        for (int i = 1; i <= RECORD_COUNT; i++)
        {
            users.Add((
                id: i,
                name: $"User{i}",
                email: $"user{i}@example.com",
                age: 18 + (i % 48), // Ages 18-65
                createdAt: DateTime.UtcNow.AddDays(-i),
                isActive: i % 2 == 0
            ));
        }

        // Use true batch insert for fast setup
        db.InsertUsersTrueBatch(users);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dbFullScan?.Dispose();
        _dbHashIndex?.Dispose();
        _dbBTreeIndex?.Dispose();

        CleanupDatabase(DB_PATH_FULLSCAN);
        CleanupDatabase(DB_PATH_HASH);
        CleanupDatabase(DB_PATH_BTREE);
    }

    private static void CleanupDatabase(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    // ==================== RANGE QUERY BENCHMARKS ====================

    [Benchmark(Baseline = true)]
    public void FullTableScan_RangeQuery()
    {
        // SELECT * FROM users WHERE age > 30
        // Expected: ~30ms (full scan of 10k records)
        var db = ((SharpCoreDB.Database)((dynamic)_dbFullScan!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 30");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void HashIndex_RangeQuery()
    {
        // SELECT * FROM users WHERE age > 30 (with hash index)
        // Expected: ~30ms (hash index doesn't help with range queries, falls back to scan)
        var db = ((SharpCoreDB.Database)((dynamic)_dbHashIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 30");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void BTreeIndex_RangeQuery()
    {
        // SELECT * FROM users WHERE age > 30 (with B-tree index)
        // Expected: < 10ms (3-5x faster than full scan)
        var db = ((SharpCoreDB.Database)((dynamic)_dbBTreeIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 30");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void BTreeIndex_BetweenQuery()
    {
        // SELECT * FROM users WHERE age BETWEEN 25 AND 35
        // Expected: < 15ms (selective range query)
        var db = ((SharpCoreDB.Database)((dynamic)_dbBTreeIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age >= 25 AND age <= 35");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void BTreeIndex_OrderBy()
    {
        // SELECT * FROM users ORDER BY age
        // Expected: < 5ms (B-tree provides sorted iteration, no external sort needed)
        var db = ((SharpCoreDB.Database)((dynamic)_dbBTreeIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users ORDER BY age");
        
        if (results.Count != RECORD_COUNT)
            throw new InvalidOperationException($"Expected {RECORD_COUNT} results, got {results.Count}");
    }

    [Benchmark]
    public void FullTableScan_OrderBy()
    {
        // SELECT * FROM users ORDER BY age (without B-tree index)
        // Expected: ~40ms (full scan + external sort overhead)
        var db = ((SharpCoreDB.Database)((dynamic)_dbFullScan!).database);
        var results = db.ExecuteQuery("SELECT * FROM users ORDER BY age");
        
        if (results.Count != RECORD_COUNT)
            throw new InvalidOperationException($"Expected {RECORD_COUNT} results, got {results.Count}");
    }

    // ==================== POINT LOOKUP COMPARISON ====================

    [Benchmark]
    public void HashIndex_PointLookup()
    {
        // SELECT * FROM users WHERE age = 30 (with hash index)
        // Expected: < 1ms (O(1) hash lookup)
        var db = ((SharpCoreDB.Database)((dynamic)_dbHashIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age = 30");
    }

    [Benchmark]
    public void BTreeIndex_PointLookup()
    {
        // SELECT * FROM users WHERE age = 30 (with B-tree index)
        // Expected: ~2ms (O(log n) B-tree lookup, slower than hash but still fast)
        var db = ((SharpCoreDB.Database)((dynamic)_dbBTreeIndex!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age = 30");
    }
}
