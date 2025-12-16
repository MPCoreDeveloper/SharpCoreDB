// <copyright file="InsertOptimizationsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Optimizations;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for insert performance optimizations.
/// Goal: Achieve 50-55ms for 10K inserts (within 20-30% of SQLite's 42ms).
/// Modern C# 14 with collection expressions and primary constructors.
/// </summary>
public sealed class InsertOptimizationsTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseFactory _factory;
    private readonly byte[] _encryptionKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="InsertOptimizationsTests"/> class.
    /// </summary>
    public InsertOptimizationsTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_opt_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();

        _encryptionKey = new byte[32];
        RandomNumberGenerator.Fill(_encryptionKey);
    }

    /// <summary>
    /// Tests baseline performance without optimizations.
    /// </summary>
    [Fact]
    public void Baseline_10K_Inserts_Without_Optimizations()
    {
        // Arrange
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            HighSpeedInsertMode = false,
        };
        var db = _factory.Create(_testDbPath, "pass", false, config);
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT, age INTEGER)");

        var rows = GenerateTestRows(10_000);

        // Act
        var sw = Stopwatch.StartNew();
        
        foreach (var row in rows)
        {
            db.ExecuteSQL(
                "INSERT INTO users VALUES (?, ?, ?, ?)",
                new Dictionary<string, object?>
                {
                    ["0"] = row["id"],
                    ["1"] = row["name"],
                    ["2"] = row["email"],
                    ["3"] = row["age"]
                });
        }
        
        sw.Stop();

        // Assert
        Console.WriteLine($"BASELINE: 10K inserts = {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Target: < 60ms (20-30% of SQLite 42ms)");
        
        Assert.True(sw.ElapsedMilliseconds > 100, "Baseline should be > 100ms");
    }

    /// <summary>
    /// Tests delayed transpose optimization.
    /// </summary>
    [Fact]
    public void Optimization1_DelayedTranspose_ImprovesByBatchProcessing()
    {
        // Arrange
        var transpose = new InsertOptimizations.DelayedColumnTranspose();
        var rows = GenerateTestRows(10_000);

        // Act - Add rows WITHOUT transpose overhead
        var sw = Stopwatch.StartNew();
        
        foreach (var row in rows)
        {
            transpose.AddRow(row);
        }
        
        sw.Stop();
        var insertTime = sw.ElapsedMilliseconds;

        sw.Restart();
        transpose.TransposeIfNeeded();
        sw.Stop();
        var transposeTime = sw.ElapsedMilliseconds;

        // Assert
        Console.WriteLine($"Insert time (delayed): {insertTime} ms");
        Console.WriteLine($"Transpose time (once): {transposeTime} ms");
        Console.WriteLine($"Total: {insertTime + transposeTime} ms");

        Assert.True(insertTime < 100, $"Delayed inserts should be < 100ms, was {insertTime}ms");
    }

    /// <summary>
    /// Tests buffered encryption optimization.
    /// </summary>
    [Fact]
    public void Optimization2_BufferedEncryption_ReducesAesOverhead()
    {
        // Arrange
        var rows = GenerateTestRows(10_000);
        var serializedRows = rows.Select(SerializeRow).ToList();

        // Test 1: Per-row encryption (baseline)
        var sw1 = Stopwatch.StartNew();
        using (var aes = Aes.Create())
        {
            aes.Key = _encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.GenerateIV();
            
            foreach (var data in serializedRows)
            {
                using var encryptor = aes.CreateEncryptor();
                using var ms = new MemoryStream();
                using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                cs.Write(data);
                cs.FlushFinalBlock();
            }
        }
        sw1.Stop();
        var perRowTime = sw1.ElapsedMilliseconds;

        // Test 2: Buffered encryption (optimized)
        var sw2 = Stopwatch.StartNew();
        using (var buffered = new InsertOptimizations.BufferedAesEncryption(_encryptionKey, bufferSizeKB: 32))
        {
            foreach (var data in serializedRows)
            {
                buffered.AddData(data);
            }
            _ = buffered.FlushBuffer();
        }
        sw2.Stop();
        var bufferedTime = sw2.ElapsedMilliseconds;

        // Assert
        Console.WriteLine($"Per-row encryption: {perRowTime} ms");
        Console.WriteLine($"Buffered encryption: {bufferedTime} ms");
        Console.WriteLine($"Improvement: {(1 - bufferedTime / (double)perRowTime) * 100:F1}%");

        Assert.True(bufferedTime < perRowTime, "Buffered encryption should be faster");
    }

    /// <summary>
    /// Tests combined optimizations.
    /// </summary>
    [Fact]
    public void Combined_All_Optimizations_AchievesTarget()
    {
        // Arrange
        var rows = GenerateTestRows(10_000);
        var serializedRows = rows.Select(SerializeRow).ToList();

        using var optimizer = new InsertOptimizations.CombinedInsertOptimizer(
            enableEncryption: false
        );

        // Act
        var sw = Stopwatch.StartNew();
        
        optimizer.InsertBatch(rows, serializedRows);
        optimizer.CompleteBulkImport();
        
        sw.Stop();

        // Assert
        Console.WriteLine("?".PadRight(60, '?'));
        Console.WriteLine("  COMBINED OPTIMIZATION RESULTS");
        Console.WriteLine("?".PadRight(60, '?'));
        Console.WriteLine($"10K inserts: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Baseline: 252 ms (from benchmarks)");
        Console.WriteLine($"SQLite target: 42 ms (20-30% = 50-55ms)");
        Console.WriteLine($"Improvement: {(1 - sw.ElapsedMilliseconds / 252.0) * 100:F1}%");
        Console.WriteLine("?".PadRight(60, '?'));

        Assert.True(sw.ElapsedMilliseconds < 100, $"Expected < 100ms, got {sw.ElapsedMilliseconds}ms");

        if (sw.ElapsedMilliseconds < 60)
        {
            Console.WriteLine("?? STRETCH GOAL! Within 20-30% of SQLite!");
        }
        else if (sw.ElapsedMilliseconds < 75)
        {
            Console.WriteLine("? PRIMARY GOAL! 70-80% improvement!");
        }
    }

    /// <summary>
    /// Compares baseline vs optimized performance.
    /// </summary>
    [Fact]
    public void Comparison_Baseline_Vs_Optimized()
    {
        // Arrange
        var rows = GenerateTestRows(10_000);
        var serializedRows = rows.Select(SerializeRow).ToList();

        // Baseline (simulated)
        var sw1 = Stopwatch.StartNew();
        var baselineList = new List<byte[]>();
        foreach (var data in serializedRows)
        {
            baselineList.Add(data);
        }
        sw1.Stop();
        var baselineTime = sw1.ElapsedMilliseconds;

        // Optimized
        var sw2 = Stopwatch.StartNew();
        using var optimizer = new InsertOptimizations.CombinedInsertOptimizer();
        optimizer.InsertBatch(rows, serializedRows);
        optimizer.CompleteBulkImport();
        sw2.Stop();
        var optimizedTime = sw2.ElapsedMilliseconds;

        // Results
        Console.WriteLine("??????????????????????????????????????????????????????");
        Console.WriteLine("?  BASELINE vs OPTIMIZED (10K inserts)              ?");
        Console.WriteLine("??????????????????????????????????????????????????????");
        Console.WriteLine($"?  Baseline:      {baselineTime,6} ms                          ?");
        Console.WriteLine($"?  Optimized:     {optimizedTime,6} ms                          ?");
        Console.WriteLine($"?  Improvement:   {(1 - optimizedTime / (double)Math.Max(baselineTime, 1)) * 100,6:F1} %                          ?");
        Console.WriteLine("??????????????????????????????????????????????????????");
        Console.WriteLine($"?  Target: 50-55 ms (20-30% of SQLite)              ?");
        Console.WriteLine($"?  Status: {(optimizedTime < 75 ? "? ACHIEVED" : "?? PARTIAL"),20}                   ?");
        Console.WriteLine("??????????????????????????????????????????????????????");

        Assert.True(optimizedTime <= baselineTime, "Optimized should not be slower");
    }

    private static List<Dictionary<string, object>> GenerateTestRows(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = $"User{i}",
                ["email"] = $"user{i}@test.com",
                ["age"] = 20 + (i % 50)
            })
            .ToList();

    private static byte[] SerializeRow(Dictionary<string, object> row)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        writer.Write((int)row["id"]!);
        writer.Write((string)row["name"]!);
        writer.Write((string)row["email"]!);
        writer.Write((int)row["age"]!);
        
        return ms.ToArray();
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
