// <copyright file="PageSerializer.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Core.File;

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpCoreDB.Services;

/// <summary>
/// Zero-allocation page serializer using MemoryMarshal, stackalloc, and BinaryPrimitives.
/// Eliminates all byte[] allocations in hot paths.
/// </summary>
public static class PageSerializer
{
    private const int PageSize = 4096;
    private const int HeaderSize = PageHeader.Size;
    private const int MaxDataSize = PageSize - HeaderSize;

    /// <summary>
    /// Serializes a page header to a span using MemoryMarshal.Cast for zero-allocation.
    /// </summary>
    /// <param name="header">The page header to serialize.</param>
    /// <param name="destination">The destination span (must be at least PageHeader.Size bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SerializeHeader(ref PageHeader header, Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException($"Destination must be at least {HeaderSize} bytes", nameof(destination));

        // OPTIMIZED: Use MemoryMarshal.Cast for zero-copy struct serialization
        var headerSpan = MemoryMarshal.CreateReadOnlySpan(ref header, 1);
        var headerBytes = MemoryMarshal.AsBytes(headerSpan);
        headerBytes.CopyTo(destination);
    }

    /// <summary>
    /// Deserializes a page header from a span using MemoryMarshal.Cast for zero-allocation.
    /// </summary>
    /// <param name="source">The source span (must be at least PageHeader.Size bytes).</param>
    /// <returns>The deserialized page header.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PageHeader DeserializeHeader(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
            throw new ArgumentException($"Source must be at least {HeaderSize} bytes", nameof(source));

        // OPTIMIZED: Use MemoryMarshal.Cast for zero-copy struct deserialization
        return MemoryMarshal.Read<PageHeader>(source);
    }

    /// <summary>
    /// Writes an integer to a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="value">The value to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(Span<byte> destination, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
    }

    /// <summary>
    /// Reads an integer from a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="source">The source span.</param>
    /// <returns>The read integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(source);
    }

    /// <summary>
    /// Writes a long to a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="value">The value to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(Span<byte> destination, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }

    /// <summary>
    /// Reads a long from a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="source">The source span.</param>
    /// <returns>The read long.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(source);
    }

    /// <summary>
    /// Writes a ushort to a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="value">The value to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(Span<byte> destination, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    /// <summary>
    /// Reads a ushort from a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="source">The source span.</param>
    /// <returns>The read ushort.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(source);
    }

    /// <summary>
    /// Writes a uint to a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="value">The value to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    /// <summary>
    /// Reads a uint from a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="source">The source span.</param>
    /// <returns>The read uint.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(source);
    }

    /// <summary>
    /// Writes a ulong to a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <param name="value">The value to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(Span<byte> destination, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
    }

    /// <summary>
    /// Reads a ulong from a span using BinaryPrimitives (little-endian).
    /// </summary>
    /// <param name="source">The source span.</param>
    /// <returns>The read ulong.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(source);
    }

    /// <summary>
    /// Computes a checksum for page data using SIMD-accelerated hash.
    /// </summary>
    /// <param name="pageData">The page data (excluding header).</param>
    /// <returns>The computed checksum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ComputeChecksum(ReadOnlySpan<byte> pageData)
    {
        // SIMD-accelerated hash for checksum
        return (uint)SimdHelper.ComputeHashCode(pageData);
    }

    /// <summary>
    /// Validates page integrity by checking header and checksum.
    /// </summary>
    /// <param name="page">The complete page data.</param>
    /// <returns>True if page is valid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool ValidatePage(ReadOnlySpan<byte> page)
    {
        if (page.Length < PageSize)
            return false;

        // Read and validate header
        var header = DeserializeHeader(page);
        if (!header.IsValid())
            return false;

        // Validate checksum
        var dataSpan = page.Slice(HeaderSize, page.Length - HeaderSize);
        uint actualChecksum = ComputeChecksum(dataSpan);
        return actualChecksum == header.Checksum;
    }

    /// <summary>
    /// Creates a complete page with header and data using stackalloc for small pages.
    /// </summary>
    /// <param name="header">The page header.</param>
    /// <param name="data">The page data.</param>
    /// <param name="destination">The destination buffer (must be PageSize bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void CreatePage(ref PageHeader header, ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (destination.Length < PageSize)
            throw new ArgumentException($"Destination must be {PageSize} bytes", nameof(destination));

        if (data.Length > MaxDataSize)
            throw new ArgumentException($"Data too large: {data.Length} > {MaxDataSize}", nameof(data));

        // Compute checksum
        header.Checksum = ComputeChecksum(data);
        header.EntryCount = (ushort)data.Length;
        header.FreeSpaceOffset = (ushort)(HeaderSize + data.Length);

        // Serialize header
        SerializeHeader(ref header, destination);

        // Copy data
        data.CopyTo(destination.Slice(HeaderSize));

        // Zero remaining space (SIMD-accelerated)
        int remainingSize = PageSize - HeaderSize - data.Length;
        if (remainingSize > 0)
        {
            SimdHelper.ZeroBuffer(destination.Slice(HeaderSize + data.Length, remainingSize));
        }
    }

    /// <summary>
    /// Extracts data from a page (excluding header).
    /// </summary>
    /// <param name="page">The complete page.</param>
    /// <param name="dataLength">Output: the length of data.</param>
    /// <returns>A span pointing to the data section.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> GetPageData(ReadOnlySpan<byte> page, out int dataLength)
    {
        if (page.Length < PageSize)
            throw new ArgumentException("Invalid page size", nameof(page));

        var header = DeserializeHeader(page);
        dataLength = header.EntryCount;
        return page.Slice(HeaderSize, dataLength);
    }

    /// <summary>
    /// Gets the page size constant.
    /// </summary>
    public static int GetPageSize() => PageSize;

    /// <summary>
    /// Gets the header size constant.
    /// </summary>
    public static int GetHeaderSize() => HeaderSize;

    /// <summary>
    /// Gets the maximum data size per page.
    /// </summary>
    public static int GetMaxDataSize() => MaxDataSize;
}
