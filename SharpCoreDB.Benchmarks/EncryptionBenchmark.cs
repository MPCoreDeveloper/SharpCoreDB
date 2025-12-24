// <copyright file="EncryptionBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using SharpCoreDB.Optimizations;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Encryption overhead benchmark for batch AES-256-GCM operations.
/// TARGET: 10k encrypted inserts from 666ms to less than 100ms (6.6x speedup).
/// 
/// Shows the performance impact of different encryption strategies:
/// 1. Per-row encryption (baseline): ~666ms for 10k inserts
/// 2. Batch encryption (optimized): target less than 100ms (6.6x faster)
/// 3. Unencrypted baseline: reference point for maximum throughput
/// </summary>
public static class EncryptionBenchmark
{
    private const int RowCount = 10_000;
    private const int RowSize = 256; // Average serialized row size

    /// <summary>
    /// Performance benchmark entry point.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("════════════════════════════════════════════════════════════════");
        Console.WriteLine("  ENCRYPTION OVERHEAD BENCHMARK - AES-256-GCM Optimization");
        Console.WriteLine("════════════════════════════════════════════════════════════════\n");
        
        Console.WriteLine("Target: 10,000 encrypted inserts");
        Console.WriteLine("Baseline (current): 666ms");
        Console.WriteLine("Target (batch): <100ms (6.6x improvement)");
        Console.WriteLine("────────────────────────────────────────────────────────────────\n");

        // Generate test data
        var testData = GenerateTestData(RowCount, RowSize);
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        // Run benchmarks
        BenchmarkPerRowEncryption(testData, key);
        BenchmarkBatchEncryption(testData, key);
        BenchmarkUnencryptedBaseline(testData);

        Console.WriteLine("\n════════════════════════════════════════════════════════════════");
        Console.WriteLine("Encryption Benchmark Complete!");
        Console.WriteLine("════════════════════════════════════════════════════════════════");
    }

    /// <summary>
    /// Benchmark per-row encryption (baseline).
    /// </summary>
    private static void BenchmarkPerRowEncryption(List<byte[]> testData, byte[] key)
    {
        Console.WriteLine("\n📊 BENCHMARK 1: Per-Row Encryption (Baseline)");
        Console.WriteLine("────────────────────────────────────────────");
        
        // Warm up
        using var warmup = new System.Security.Cryptography.AesGcm(key, 16);
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> tag = stackalloc byte[16];
        var cipher = new byte[256];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
        warmup.Encrypt(nonce, testData[0], cipher, tag);

        // Benchmark - pre-allocate buffers outside loop
        var sw = Stopwatch.StartNew();
        long totalBytes = 0;
        int encryptionCount = 0;

        using (var aes = new System.Security.Cryptography.AesGcm(key, 16))
        {
            Span<byte> rowNonce = stackalloc byte[12];
            Span<byte> rowTag = stackalloc byte[16];
            
            foreach (var rowData in testData)
            {
                var rowCipher = new byte[rowData.Length];
                
                System.Security.Cryptography.RandomNumberGenerator.Fill(rowNonce);
                aes.Encrypt(rowNonce, rowData, rowCipher, rowTag);
                
                totalBytes += rowData.Length;
                encryptionCount++;
            }
        }

        sw.Stop();

        // Results
        double throughput = (totalBytes / 1024.0 / 1024.0) / (sw.ElapsedMilliseconds / 1000.0);
        double timePerRow = (double)sw.ElapsedMilliseconds / RowCount;
        
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Rows: {RowCount:N0}");
        Console.WriteLine($"  Total data: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  Throughput: {throughput:F2} MB/s");
        Console.WriteLine($"  Per-row: {timePerRow:F3} ms");
        Console.WriteLine($"  Operations: {encryptionCount:N0}");
    }

    /// <summary>
    /// Benchmark batch encryption (optimized).
    /// </summary>
    private static void BenchmarkBatchEncryption(List<byte[]> testData, byte[] key)
    {
        Console.WriteLine("\n📊 BENCHMARK 2: Batch Encryption (Optimized)");
        Console.WriteLine("────────────────────────────────────────────");
        
        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        using (var buffered = new BufferedAesEncryption(key, batchSizeKB: 64))
        {
            int batchCount = 0;
            
            // Add rows to batch
            foreach (var rowData in testData)
            {
                if (!buffered.AddPlaintext(rowData))
                {
                    // Batch full - encrypt and reset
                    _ = buffered.FlushBatch();
                    batchCount++;
                    
                    // Add current row to new batch
                    buffered.AddPlaintext(rowData);
                }
                
                totalBytes += rowData.Length;
            }
            
            // Encrypt final batch
            if (buffered.HasPendingData)
            {
                _ = buffered.FlushBatch();
                batchCount++;
            }
            
            Console.WriteLine($"  Batch operations: {batchCount:N0}");
        }

        sw.Stop();

        // Results
        double throughput = (totalBytes / 1024.0 / 1024.0) / (sw.ElapsedMilliseconds / 1000.0);
        double timePerRow = (double)sw.ElapsedMilliseconds / RowCount;
        
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Rows: {RowCount:N0}");
        Console.WriteLine($"  Total data: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  Throughput: {throughput:F2} MB/s");
        Console.WriteLine($"  Per-row: {timePerRow:F3} ms");
    }

    /// <summary>
    /// Benchmark unencrypted baseline.
    /// </summary>
    private static void BenchmarkUnencryptedBaseline(List<byte[]> testData)
    {
        Console.WriteLine("\n📊 BENCHMARK 3: Unencrypted Baseline");
        Console.WriteLine("────────────────────────────────────");
        
        var sw = Stopwatch.StartNew();
        long totalBytes = 0;

        // Just copy data (simulating unencrypted write)
        foreach (var rowData in testData)
        {
            var copy = new byte[rowData.Length];
            rowData.CopyTo(copy, 0);
            totalBytes += rowData.Length;
        }

        sw.Stop();

        // Results
        double throughput = (totalBytes / 1024.0 / 1024.0) / (sw.ElapsedMilliseconds / 1000.0);
        double timePerRow = (double)sw.ElapsedMilliseconds / RowCount;
        
        Console.WriteLine($"  Time: {sw.ElapsedMilliseconds} ms (reference only)");
        Console.WriteLine($"  Rows: {RowCount:N0}");
        Console.WriteLine($"  Total data: {totalBytes / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  Throughput: {throughput:F2} MB/s");
        Console.WriteLine($"  Per-row: {timePerRow:F3} ms");
    }

    /// <summary>
    /// Generates test data for benchmarking.
    /// </summary>
    private static List<byte[]> GenerateTestData(int rowCount, int rowSize)
    {
        var data = new List<byte[]>(rowCount);
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();

        for (int i = 0; i < rowCount; i++)
        {
            var row = new byte[rowSize];
            rng.GetBytes(row);
            data.Add(row);
        }

        rng.Dispose();
        return data;
    }
}
