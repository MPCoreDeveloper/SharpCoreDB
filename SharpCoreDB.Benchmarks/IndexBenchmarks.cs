// <copyright file="IndexBenchmarks.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using SharpCoreDB.Pooling;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for hash index operations.
/// Compares traditional dictionary vs. optimized hash index with SIMD.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class IndexBenchmarks
{
    private HashIndex hashIndex = null!;
    private Dictionary<string, List<int>> dictionaryIndex = null!;
    private TemporaryBufferPool bufferPool = null!;
    private string[] keys = null!;
    private Dictionary<string, object>[] rows = null!;

    [Params(100, 1000, 10000)]
    public int IndexSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate test data
        keys = new string[IndexSize];
        rows = new Dictionary<string, object>[IndexSize];
        
        for (int i = 0; i < IndexSize; i++)
        {
            keys[i] = $"key_{i:D6}";
            rows[i] = new Dictionary<string, object>
            {
                { "test_column", keys[i] },
                { "value", i }
            };
        }

        // Initialize hash index
        hashIndex = new HashIndex("test_table", "test_column");
        for (int i = 0; i < IndexSize; i++)
        {
            hashIndex.Add(rows[i], i);
        }

        // Initialize dictionary index (baseline)
        dictionaryIndex = new Dictionary<string, List<int>>();
        for (int i = 0; i < IndexSize; i++)
        {
            if (!dictionaryIndex.TryGetValue(keys[i], out var list))
            {
                list = new List<int>();
                dictionaryIndex[keys[i]] = list;
            }
            list.Add(i);
        }

        bufferPool = new TemporaryBufferPool();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        bufferPool?.Dispose();
    }

    // ==================== LOOKUP OPERATIONS ====================

    [Benchmark(Baseline = true, Description = "Lookup: Dictionary (baseline)")]
    public int Lookup_Dictionary()
    {
        int found = 0;
        for (int i = 0; i < 100; i++)
        {
            int idx = Random.Shared.Next(IndexSize);
            if (dictionaryIndex.TryGetValue(keys[idx], out var list))
            {
                found += list.Count;
            }
        }
        return found;
    }

    [Benchmark(Description = "Lookup: HashIndex (optimized)")]
    public int Lookup_HashIndex()
    {
        int found = 0;
        for (int i = 0; i < 100; i++)
        {
            int idx = Random.Shared.Next(IndexSize);
            var result = hashIndex.LookupPositions(keys[idx]);
            found += result.Count;
        }
        return found;
    }

    // ==================== INSERT OPERATIONS ====================

    [Benchmark(Description = "Insert: Dictionary (allocates)")]
    public int Insert_Dictionary()
    {
        var index = new Dictionary<string, List<int>>();
        
        for (int i = 0; i < 100; i++)
        {
            var key = $"new_key_{i}";
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<int>();
                index[key] = list;
            }
            list.Add(i);
        }
        
        return index.Count;
    }

    [Benchmark(Description = "Insert: HashIndex (pooled)")]
    public int Insert_HashIndex()
    {
        var index = new HashIndex("test_table", "test_column");
        
        for (int i = 0; i < 100; i++)
        {
            var row = new Dictionary<string, object>
            {
                { "test_column", $"new_key_{i}" }
            };
            index.Add(row, i);
        }
        
        return index.Count;
    }

    // ==================== HASH COMPUTATION ====================

    [Benchmark(Description = "Hash: GetHashCode (traditional)")]
    public int Hash_GetHashCode()
    {
        int total = 0;
        for (int i = 0; i < 1000; i++)
        {
            total += keys[i % IndexSize].GetHashCode();
        }
        return total;
    }

    [Benchmark(Description = "Hash: SIMD (optimized)")]
    public int Hash_Simd()
    {
        int total = 0;
        using var buffer = bufferPool.RentSmallByteBuffer();
        
        for (int i = 0; i < 1000; i++)
        {
            var key = keys[i % IndexSize];
            int byteCount = System.Text.Encoding.UTF8.GetBytes(key, buffer.AsSpan());
            total += SimdHelper.ComputeHashCode(buffer.ByteBuffer.AsSpan(0, byteCount));
        }
        return total;
    }

    // ==================== RANGE SCAN ====================

    [Benchmark(Description = "Scan: Dictionary values")]
    public int Scan_Dictionary()
    {
        int total = 0;
        foreach (var kvp in dictionaryIndex)
        {
            total += kvp.Value.Count;
        }
        return total;
    }

    [Benchmark(Description = "Scan: HashIndex statistics")]
    public int Scan_HashIndex()
    {
        var stats = hashIndex.GetStatistics();
        return stats.TotalRows;
    }

    // ==================== REMOVE OPERATIONS ====================

    [Benchmark(Description = "Remove: Dictionary")]
    public int Remove_Dictionary()
    {
        var index = new Dictionary<string, List<int>>(dictionaryIndex);
        
        for (int i = 0; i < 10; i++)
        {
            index.Remove(keys[i]);
        }
        
        return index.Count;
    }

    [Benchmark(Description = "Remove: HashIndex")]
    public int Remove_HashIndex()
    {
        var index = new HashIndex("test_table", "test_column");
        for (int i = 0; i < IndexSize; i++)
        {
            index.Add(rows[i], i);
        }
        
        for (int i = 0; i < 10; i++)
        {
            index.Remove(rows[i]);
        }
        
        return index.Count;
    }

    // ==================== STRING KEY COMPARISON ====================

    [Benchmark(Description = "String Compare: Traditional (equals)")]
    public int StringCompare_Traditional()
    {
        int matches = 0;
        for (int i = 0; i < 1000; i++)
        {
            var key1 = keys[i % IndexSize];
            var key2 = keys[(i + 1) % IndexSize];
            if (key1.Equals(key2, StringComparison.Ordinal))
                matches++;
        }
        return matches;
    }

    [Benchmark(Description = "String Compare: SIMD (vectorized)")]
    public int StringCompare_Simd()
    {
        int matches = 0;
        using var buffer1 = bufferPool.RentSmallByteBuffer();
        using var buffer2 = bufferPool.RentSmallByteBuffer();
        
        for (int i = 0; i < 1000; i++)
        {
            var key1 = keys[i % IndexSize];
            var key2 = keys[(i + 1) % IndexSize];
            
            // Convert to bytes for SIMD comparison
            int len1 = System.Text.Encoding.UTF8.GetBytes(key1, buffer1.ByteBuffer.AsSpan());
            int len2 = System.Text.Encoding.UTF8.GetBytes(key2, buffer2.ByteBuffer.AsSpan());
            
            if (len1 == len2 && SimdHelper.SequenceEqual(
                buffer1.ByteBuffer.AsSpan(0, len1),
                buffer2.ByteBuffer.AsSpan(0, len2)))
                matches++;
        }
        return matches;
    }

    // ==================== CONTAINS CHECK ====================

    [Benchmark(Description = "Contains: Dictionary")]
    public int Contains_Dictionary()
    {
        int found = 0;
        for (int i = 0; i < 1000; i++)
        {
            var key = keys[i % IndexSize];
            if (dictionaryIndex.ContainsKey(key))
                found++;
        }
        return found;
    }

    [Benchmark(Description = "Contains: HashIndex")]
    public int Contains_HashIndex()
    {
        int found = 0;
        for (int i = 0; i < 1000; i++)
        {
            var key = keys[i % IndexSize];
            if (hashIndex.ContainsKey(key))
                found++;
        }
        return found;
    }
}
