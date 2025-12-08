// <copyright file="PageSerializationBenchmarks.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Core.File;
using SharpCoreDB.Pooling;
using System.Buffers;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for page serialization/deserialization.
/// Compares traditional allocation-based approach vs. optimized Span/MemoryMarshal approach.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class PageSerializationBenchmarks
{
    private PageHeader header;
    private byte[] pageData = null!;
    private byte[] pageBuffer = null!;
    private PageSerializerPool pool = null!;
    private ArrayPool<byte> arrayPool = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create test data using correct PageHeader API
        header = PageHeader.Create((byte)PageType.Data, 12345);
        header.EntryCount = 50;
        header.FreeSpaceOffset = 1024;
        header.NextPageId = 12346;

        // 3KB of test data
        pageData = new byte[3072];
        Random.Shared.NextBytes(pageData);

        pageBuffer = new byte[4096];
        pool = new PageSerializerPool(4096);
        arrayPool = ArrayPool<byte>.Shared;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        pool?.Dispose();
    }

    // ==================== HEADER SERIALIZATION ====================

    [Benchmark(Baseline = true, Description = "Header: Traditional (allocates)")]
    public byte[] SerializeHeader_Traditional()
    {
        // Traditional approach - allocates byte array
        var buffer = new byte[PageHeader.Size];
        
        // Manual serialization (simplified)
        BitConverter.TryWriteBytes(buffer.AsSpan(0), header.MagicNumber);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), header.Version);
        buffer[6] = header.PageType;
        buffer[7] = header.Flags;
        BitConverter.TryWriteBytes(buffer.AsSpan(8), header.EntryCount);
        BitConverter.TryWriteBytes(buffer.AsSpan(10), header.FreeSpaceOffset);
        BitConverter.TryWriteBytes(buffer.AsSpan(12), header.Checksum);
        
        return buffer;
    }

    [Benchmark(Description = "Header: Optimized (MemoryMarshal)")]
    public void SerializeHeader_Optimized()
    {
        // Optimized approach - uses MemoryMarshal, no allocation
        var localHeader = header;
        PageSerializer.SerializeHeader(ref localHeader, pageBuffer.AsSpan());
    }

    [Benchmark(Description = "Header: Pooled Buffer")]
    public void SerializeHeader_Pooled()
    {
        using var rented = pool.Rent();
        var localHeader = header;
        rented.SerializeHeader(ref localHeader);
    }

    // ==================== HEADER DESERIALIZATION ====================

    [Benchmark(Description = "Deserialize Header: Traditional")]
    public PageHeader DeserializeHeader_Traditional()
    {
        // Traditional approach - multiple operations
        return new PageHeader
        {
            MagicNumber = BitConverter.ToUInt32(pageBuffer, 0),
            Version = BitConverter.ToUInt16(pageBuffer, 4),
            PageType = pageBuffer[6],
            Flags = pageBuffer[7],
            EntryCount = BitConverter.ToUInt16(pageBuffer, 8),
            FreeSpaceOffset = BitConverter.ToUInt16(pageBuffer, 10),
            Checksum = BitConverter.ToUInt32(pageBuffer, 12),
        };
    }

    [Benchmark(Description = "Deserialize Header: Optimized (MemoryMarshal)")]
    public PageHeader DeserializeHeader_Optimized()
    {
        // Optimized approach - MemoryMarshal.Read, zero allocation
        return PageSerializer.DeserializeHeader(pageBuffer.AsSpan());
    }

    // ==================== FULL PAGE OPERATIONS ====================

    [Benchmark(Description = "Create Page: Traditional (multiple allocs)")]
    public byte[] CreatePage_Traditional()
    {
        var buffer = new byte[4096];
        var localHeader = header;
        
        // Serialize header
        var headerBytes = SerializeHeader_Traditional();
        headerBytes.CopyTo(buffer, 0);
        
        // Copy data
        pageData.CopyTo(buffer, PageHeader.Size);
        
        // Zero remaining space
        Array.Clear(buffer, PageHeader.Size + pageData.Length, 
                   4096 - PageHeader.Size - pageData.Length);
        
        return buffer;
    }

    [Benchmark(Description = "Create Page: Optimized (Span + SIMD)")]
    public void CreatePage_Optimized()
    {
        var localHeader = header;
        PageSerializer.CreatePage(ref localHeader, pageData, pageBuffer.AsSpan());
    }

    [Benchmark(Description = "Create Page: Pooled Buffer")]
    public void CreatePage_Pooled()
    {
        using var rented = pool.Rent();
        var localHeader = header;
        rented.CreatePage(ref localHeader, pageData);
    }

    // ==================== CHECKSUM COMPUTATION ====================

    [Benchmark(Description = "Checksum: Traditional (naive loop)")]
    public uint ComputeChecksum_Traditional()
    {
        // Traditional approach - simple loop
        uint checksum = 0;
        for (int i = 0; i < pageData.Length; i++)
        {
            checksum = unchecked(checksum * 31 + pageData[i]);
        }
        return checksum;
    }

    [Benchmark(Description = "Checksum: Optimized (SIMD)")]
    public uint ComputeChecksum_Optimized()
    {
        // SIMD-accelerated checksum
        return PageSerializer.ComputeChecksum(pageData);
    }

    // ==================== PAGE VALIDATION ====================

    [Benchmark(Description = "Validate Page: Traditional")]
    public bool ValidatePage_Traditional()
    {
        // Read header manually
        var readHeader = DeserializeHeader_Traditional();
        
        // Validate
        if (!readHeader.IsValid())
            return false;
        
        // Compute checksum manually
        var checksum = ComputeChecksum_Traditional();
        return checksum == readHeader.Checksum;
    }

    [Benchmark(Description = "Validate Page: Optimized")]
    public bool ValidatePage_Optimized()
    {
        // Optimized validation with SIMD checksum
        return PageSerializer.ValidatePage(pageBuffer);
    }

    // ==================== ROUND-TRIP BENCHMARK ====================

    [Benchmark(Description = "Round-trip: Traditional (full alloc)")]
    public PageHeader RoundTrip_Traditional()
    {
        // Serialize
        var buffer = CreatePage_Traditional();
        
        // Deserialize
        return DeserializeHeader_Traditional();
    }

    [Benchmark(Description = "Round-trip: Optimized (zero alloc)")]
    public PageHeader RoundTrip_Optimized()
    {
        // Serialize
        var localHeader = header;
        PageSerializer.CreatePage(ref localHeader, pageData, pageBuffer.AsSpan());
        
        // Deserialize
        return PageSerializer.DeserializeHeader(pageBuffer);
    }

    [Benchmark(Description = "Round-trip: Pooled (optimal)")]
    public PageHeader RoundTrip_Pooled()
    {
        using var rented = pool.Rent();
        
        // Serialize
        var localHeader = header;
        rented.CreatePage(ref localHeader, pageData);
        
        // Deserialize
        return rented.DeserializeHeader();
    }
}
