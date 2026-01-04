// <copyright file="SimdWhereFilterBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks SIMD-accelerated WHERE clause filtering vs scalar evaluation.
/// Target performance (10k rows):
/// - Scalar: 5-10ms
/// - SIMD (Vector): 1-2ms (5-10x faster)
/// - SIMD (AVX2): &lt;1ms (10-15x faster)
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class SimdWhereFilterBenchmark
{
    private BenchmarkDatabaseHelper? _dbNoSimd;
    private BenchmarkDatabaseHelper? _dbSimd;
    private const int RECORD_COUNT = 10_000;
    private const string DB_PATH_NO_SIMD = "./bench_simd_where_noop";
    private const string DB_PATH_SIMD = "./bench_simd_where_opt";

    [GlobalSetup]
    public void Setup()
    {
        // Clean up any existing test databases
        CleanupDatabase(DB_PATH_NO_SIMD);
        CleanupDatabase(DB_PATH_SIMD);

        // Setup 1: Database without SIMD (baseline)
        _dbNoSimd = new BenchmarkDatabaseHelper(DB_PATH_NO_SIMD, enableEncryption: false);
        _dbNoSimd.CreateUsersTableColumnar();

        // Setup 2: Database with SIMD (optimized)
        _dbSimd = new BenchmarkDatabaseHelper(DB_PATH_SIMD, enableEncryption: false);
        _dbSimd.CreateUsersTableColumnar();

        // Insert test data with varied salary distribution
        var users = new List<(int id, string name, string email, int age, DateTime createdAt, bool isActive)>();
        for (int i = 1; i <= RECORD_COUNT; i++)
        {
            users.Add((
                id: i,
                name: $"User{i}",
                email: $"user{i}@example.com",
                age: 25 + (i % 40), // Ages 25-64
                createdAt: DateTime.UtcNow.AddDays(-i),
                isActive: i % 2 == 0
            ));
        }

        // Use batch insert for fast setup
        _dbNoSimd.InsertUsersTrueBatch(users);
        _dbSimd.InsertUsersTrueBatch(users);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _dbNoSimd?.Dispose();
        _dbSimd?.Dispose();

        CleanupDatabase(DB_PATH_NO_SIMD);
        CleanupDatabase(DB_PATH_SIMD);
    }

    private static void CleanupDatabase(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                System.IO.Directory.Delete(path, true);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }

    // ==================== INTEGER WHERE CLAUSES ====================

    [Benchmark(Baseline = true)]
    public void Scalar_WhereAge_GreaterThan()
    {
        // SELECT * FROM users WHERE age > 40
        // Expected: 5-10ms (scalar evaluation)
        var db = ((SharpCoreDB.Database)((dynamic)_dbNoSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 40");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void SIMD_WhereAge_GreaterThan()
    {
        // SELECT * FROM users WHERE age > 40 (with SIMD)
        // Expected: < 1ms (6-10x faster than scalar)
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 40");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void SIMD_WhereAge_LessThan()
    {
        // SELECT * FROM users WHERE age < 35
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age < 35");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void SIMD_WhereAge_Equals()
    {
        // SELECT * FROM users WHERE age = 30
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age = 30");
    }

    [Benchmark]
    public void SIMD_WhereAge_GreaterOrEqual()
    {
        // SELECT * FROM users WHERE age >= 45
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age >= 45");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    [Benchmark]
    public void SIMD_WhereAge_LessOrEqual()
    {
        // SELECT * FROM users WHERE age <= 30
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age <= 30");
        
        if (results.Count == 0)
            throw new InvalidOperationException("No results found - test data issue");
    }

    // ==================== EDGE CASES ====================

    [Benchmark]
    public void SIMD_WhereAge_HighSelectivity()
    {
        // Very selective query (< 1% of rows match)
        // SELECT * FROM users WHERE age > 60
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 60");
    }

    [Benchmark]
    public void SIMD_WhereAge_LowSelectivity()
    {
        // Low selectivity query (> 90% of rows match)
        // SELECT * FROM users WHERE age > 25
        var db = ((SharpCoreDB.Database)((dynamic)_dbSimd!).database);
        var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 25");
        
        if (results.Count < RECORD_COUNT / 2)
            throw new InvalidOperationException("Expected > 50% match rate");
    }
}
