// <copyright file="WalBenchmarks.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Pooling;
using SharpCoreDB.Services;
using System.Buffers;
using System.Text;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for WAL (Write-Ahead Log) operations.
/// Compares traditional allocation vs. pooled buffer + Span-based UTF8 encoding.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class WalBenchmarks
{
    private const int EntriesPerBatch = 1000;
    private const int BufferSize = 4 * 1024 * 1024; // 4MB
    
    private string[] operations = null!;
    private WalBufferPool bufferPool = null!;
    private ArrayPool<byte> arrayPool = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Generate test operations
        operations = new string[EntriesPerBatch];
        for (int i = 0; i < EntriesPerBatch; i++)
        {
            operations[i] = $"INSERT INTO users (id, name, email) VALUES ({i}, 'User{i}', 'user{i}@example.com')";
        }

        bufferPool = new WalBufferPool(BufferSize);
        arrayPool = ArrayPool<byte>.Shared;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        bufferPool?.Dispose();
    }

    // ==================== SINGLE ENTRY ENCODING ====================

    [Benchmark(Baseline = true, Description = "Single Entry: Traditional (allocates)")]
    public byte[] EncodeEntry_Traditional()
    {
        // Traditional: Encoding.UTF8.GetBytes allocates byte[]
        var operation = operations[0];
        var bytes = Encoding.UTF8.GetBytes(operation + "\n");
        return bytes;
    }

    [Benchmark(Description = "Single Entry: Optimized (GetByteCount + Span)")]
    public int EncodeEntry_Optimized()
    {
        var rented = bufferPool.Rent(1024);
        var operation = operations[0];
        
        // OPTIMIZED: Calculate size first, encode to span
        int byteCount = Encoding.UTF8.GetByteCount(operation);
        int bytesWritten = Encoding.UTF8.GetBytes(operation, rented.AsSpan());
        rented.AsSpan()[bytesWritten] = (byte)'\n';
        int usedSize = bytesWritten + 1;
        
        rented.UsedSize = usedSize;
        rented.Dispose();
        return usedSize;
    }

    [Benchmark(Description = "Single Entry: Pooled Buffer (optimal)")]
    public int EncodeEntry_Pooled()
    {
        var rented = bufferPool.Rent(1024);
        var operation = operations[0];
        
        // Use pooled buffer for encoding
        Span<byte> span = rented.AsSpan();
        int bytesWritten = Encoding.UTF8.GetBytes(operation, span);
        span[bytesWritten] = (byte)'\n';
        int usedSize = bytesWritten + 1;
        
        rented.UsedSize = usedSize;
        rented.Dispose();
        return usedSize;
    }

    // ==================== BATCH ENCODING ====================

    [Benchmark(Description = "Batch (1K): Traditional (allocates each)")]
    public long BatchEncode_Traditional()
    {
        long totalBytes = 0;
        
        for (int i = 0; i < EntriesPerBatch; i++)
        {
            // Each iteration allocates a new byte[]
            var bytes = Encoding.UTF8.GetBytes(operations[i] + "\n");
            totalBytes += bytes.Length;
        }
        
        return totalBytes;
    }

    [Benchmark(Description = "Batch (1K): Optimized (reuses buffer)")]
    public long BatchEncode_Optimized()
    {
        var rented = bufferPool.Rent();
        Span<byte> span = rented.AsSpan();
        long totalBytes = 0;
        int position = 0;
        
        for (int i = 0; i < EntriesPerBatch; i++)
        {
            // Calculate size
            int byteCount = Encoding.UTF8.GetByteCount(operations[i]);
            
            // Check if we need to flush
            if (position + byteCount + 1 > span.Length)
            {
                // In real WAL, would write to file here
                totalBytes += position;
                position = 0;
            }
            
            // Encode directly to span (no allocation)
            int written = Encoding.UTF8.GetBytes(operations[i], span.Slice(position));
            span[position + written] = (byte)'\n';
            position += written + 1;
        }
        
        totalBytes += position;
        rented.Dispose();
        return totalBytes;
    }

    [Benchmark(Description = "Batch (1K): Pooled with Cache (best)")]
    public long BatchEncode_PooledWithCache()
    {
        var rented = bufferPool.Rent();
        Span<byte> span = rented.AsSpan();
        long totalBytes = 0;
        int position = 0;
        
        // Pre-compute sizes (simulates caching)
        Span<int> sizes = stackalloc int[EntriesPerBatch];
        for (int i = 0; i < EntriesPerBatch; i++)
        {
            sizes[i] = Encoding.UTF8.GetByteCount(operations[i]);
        }
        
        for (int i = 0; i < EntriesPerBatch; i++)
        {
            int byteCount = sizes[i];
            
            if (position + byteCount + 1 > span.Length)
            {
                totalBytes += position;
                position = 0;
            }
            
            int written = Encoding.UTF8.GetBytes(operations[i], span.Slice(position));
            span[position + written] = (byte)'\n';
            position += written + 1;
        }
        
        totalBytes += position;
        rented.Dispose();
        return totalBytes;
    }

    // ==================== BUFFER POOL PERFORMANCE ====================

    [Benchmark(Description = "Buffer Pool: Rent/Return (thread-local)")]
    public int BufferPool_RentReturn()
    {
        var rented = bufferPool.Rent();
        int length = rented.Buffer.Length;
        rented.Dispose();
        return length;
    }

    [Benchmark(Description = "Buffer Pool: 100x Rent/Return")]
    public long BufferPool_Multiple()
    {
        long total = 0;
        for (int i = 0; i < 100; i++)
        {
            var rented = bufferPool.Rent();
            total += rented.Buffer.Length;
            rented.Dispose();
        }
        return total;
    }

    [Benchmark(Description = "ArrayPool: Rent/Return (baseline)")]
    public int ArrayPool_RentReturn()
    {
        var buffer = arrayPool.Rent(BufferSize);
        int length = buffer.Length;
        arrayPool.Return(buffer);
        return length;
    }

    // ==================== UTF8 ENCODING STRATEGIES ====================

    [Benchmark(Description = "UTF8: GetBytes (allocates)")]
    public byte[] Utf8_GetBytes()
    {
        return Encoding.UTF8.GetBytes(operations[0]);
    }

    [Benchmark(Description = "UTF8: GetBytes to Span (zero-alloc)")]
    public int Utf8_GetBytesToSpan()
    {
        Span<byte> buffer = stackalloc byte[512];
        return Encoding.UTF8.GetBytes(operations[0], buffer);
    }

    [Benchmark(Description = "UTF8: GetByteCount + GetBytes (optimal)")]
    public int Utf8_CountThenEncode()
    {
        var operation = operations[0];
        int byteCount = Encoding.UTF8.GetByteCount(operation);
        Span<byte> buffer = stackalloc byte[byteCount];
        return Encoding.UTF8.GetBytes(operation, buffer);
    }

    // ==================== BUFFER CLEARING ====================

    [Benchmark(Description = "Clear: Array.Clear (may be optimized away)")]
    public void Clear_ArrayClear()
    {
        var buffer = arrayPool.Rent(BufferSize);
        Array.Clear(buffer, 0, 1024);
        arrayPool.Return(buffer, clearArray: false);
    }

    [Benchmark(Description = "Clear: Span.Clear (guaranteed)")]
    public void Clear_SpanClear()
    {
        var buffer = arrayPool.Rent(BufferSize);
        buffer.AsSpan(0, 1024).Clear();
        arrayPool.Return(buffer, clearArray: false);
    }

    [Benchmark(Description = "Clear: SIMD Zero (fastest)")]
    public void Clear_SimdZero()
    {
        var rented = bufferPool.Rent();
        SimdHelper.ZeroBuffer(rented.Buffer.AsSpan(0, 1024));
        rented.Dispose();
    }
}
