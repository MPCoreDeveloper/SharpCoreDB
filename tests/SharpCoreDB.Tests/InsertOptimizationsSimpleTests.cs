// <copyright file="InsertOptimizationsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Diagnostics;
using System.Security.Cryptography;
using SharpCoreDB.Optimizations;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Simple tests for insert optimizations (no database dependencies).
/// </summary>
[Collection("PerformanceTests")]
public sealed class InsertOptimizationsSimpleTests
{
    [Fact]
    public void DelayedTranspose_10K_Rows_CompletesIn_LessThan_100ms()
    {
        var transpose = new InsertOptimizations.DelayedColumnTranspose();
        var rows = Enumerable.Range(0, 10_000)
            .Select(i => new Dictionary<string, object> { ["id"] = i })
            .ToList();

        var sw = Stopwatch.StartNew();
        foreach (var row in rows) transpose.AddRow(row);
        sw.Stop();

        Console.WriteLine($"? Delayed transpose: {sw.ElapsedMilliseconds} ms for 10K rows");
        Assert.True(sw.ElapsedMilliseconds < 100, $"Too slow: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void BufferedEncryption_Works_WithoutCrashing()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        
        var data = Enumerable.Range(0, 100)
            .Select(_ => new byte[50])
            .ToList();

        using var buffered = new InsertOptimizations.BufferedAesEncryption(key);
        foreach (var d in data) buffered.AddData(d);
        var encrypted = buffered.FlushBuffer();

        Assert.NotEmpty(encrypted);
        Console.WriteLine($"? Buffered encryption: {encrypted.Length} bytes");
    }

    [Fact]
    public void CombinedOptimizer_Processes_Data_Successfully()
    {
        var rows = Enumerable.Range(0, 1000)
            .Select(i => new Dictionary<string, object> { ["id"] = i })
            .ToList();
        var data = rows.Select(_ => new byte[50]).ToList();

        using var optimizer = new InsertOptimizations.CombinedInsertOptimizer();

        var sw = Stopwatch.StartNew();
        optimizer.InsertBatch(rows, data);
        optimizer.CompleteBulkImport();
        sw.Stop();

        Console.WriteLine($"? Combined optimizer: {sw.ElapsedMilliseconds} ms for 1K rows");
        Assert.True(sw.ElapsedMilliseconds < 200, $"Too slow: {sw.ElapsedMilliseconds}ms");
    }
}
