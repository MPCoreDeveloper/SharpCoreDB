// <copyright file="DeltaCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Delta encoding/decoding for efficient record updates (Phase 3.3).
/// Encodes only changed fields instead of full records.
/// C# 14: Uses Span-based operations for zero-allocation processing.
/// </summary>
public static class DeltaCodec
{
    /// <summary>
    /// Encodes the delta between old and new record data.
    /// </summary>
    /// <param name="oldData">Original record data</param>
    /// <param name="newData">Updated record data</param>
    /// <param name="destination">Buffer to write delta to</param>
    /// <returns>Number of bytes written to destination</returns>
    public static int EncodeDelta(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> newData, Span<byte> destination)
    {
        if (oldData.Length != newData.Length)
            throw new ArgumentException("Record sizes must match for delta encoding");

        var writer = new SpanWriter(destination);
        
        // Write header: total fields changed (placeholder)
        var changedCountPos = writer.Position;
        writer.Write(0); // Placeholder for count
        
        int changedCount = 0;
        
        // Compare field by field (assuming fixed-size fields for simplicity)
        // In real implementation, this would use schema information
        const int fieldSize = sizeof(long); // Example: assume 8-byte fields
        
        for (int i = 0; i < oldData.Length; i += fieldSize)
        {
            var oldField = oldData.Slice(i, fieldSize);
            var newField = newData.Slice(i, fieldSize);
            
            if (!oldField.SequenceEqual(newField))
            {
                // Write field index and new value
                writer.Write(i / fieldSize); // Field index
                writer.Write(newField); // New value
                changedCount++;
            }
        }
        
        // Update changed count
        var endPos = writer.Position;
        writer.Position = changedCountPos;
        writer.Write(changedCount);
        writer.Position = endPos;
        
        return writer.Position;
    }

    /// <summary>
    /// Applies delta to old data to get new data.
    /// </summary>
    /// <param name="oldData">Original record data</param>
    /// <param name="delta">Encoded delta</param>
    /// <param name="result">Buffer for result (must be same size as oldData)</param>
    public static void ApplyDelta(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> delta, Span<byte> result)
    {
        if (result.Length != oldData.Length)
            throw new ArgumentException("Result buffer must match old data size");

        // Copy old data as base
        oldData.CopyTo(result);
        
        var reader = new SpanReader(delta);
        
        // Read changed field count
        int changedCount = reader.ReadInt32();
        
        const int fieldSize = sizeof(long);
        
        for (int i = 0; i < changedCount; i++)
        {
            int fieldIndex = reader.ReadInt32();
            int offset = fieldIndex * fieldSize;
            
            if (offset + fieldSize > result.Length)
                throw new InvalidDataException("Invalid field index in delta");
            
            // Read new field value
            var newField = delta.Slice(reader.Position, fieldSize);
            newField.CopyTo(result.Slice(offset, fieldSize));
            reader.Position += fieldSize;
        }
    }

    /// <summary>
    /// Estimates the size of delta encoding.
    /// </summary>
    /// <param name="oldData">Original data</param>
    /// <param name="newData">New data</param>
    /// <returns>Estimated delta size in bytes</returns>
    public static int EstimateDeltaSize(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> newData)
    {
        if (oldData.Length != newData.Length) return oldData.Length; // Fallback to full size
        
        int changedFields = 0;
        const int fieldSize = sizeof(long);
        
        for (int i = 0; i < oldData.Length; i += fieldSize)
        {
            if (!oldData.Slice(i, fieldSize).SequenceEqual(newData.Slice(i, fieldSize)))
                changedFields++;
        }
        
        // Header (4) + per field (4 + fieldSize)
        return sizeof(int) + changedFields * (sizeof(int) + fieldSize);
    }
}

/// <summary>
/// Simple span-based writer for delta encoding.
/// </summary>
internal ref struct SpanWriter
{
    private readonly Span<byte> _buffer;
    public int Position { get; set; }
    
    public SpanWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }
    
    public void Write(int value) => BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(Position), value);
    public void Write(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buffer.Slice(Position));
        Position += data.Length;
    }
}

/// <summary>
/// Simple span-based reader for delta decoding.
/// </summary>
internal ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    public int Position { get; set; }
    
    public SpanReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }
    
    public int ReadInt32()
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(Position));
        Position += sizeof(int);
        return value;
    }
}

/// <summary>
/// Delta merge operations for reading records with delta chains.
/// </summary>
public static class DeltaMerge
{
    /// <summary>
    /// Merges a base record with a chain of deltas to get the current version.
    /// </summary>
    /// <param name="baseRecord">Original full record</param>
    /// <param name="deltas">List of deltas to apply in order</param>
    /// <param name="result">Buffer to write merged result</param>
    public static void MergeDeltas(ReadOnlySpan<byte> baseRecord, IReadOnlyList<ReadOnlyMemory<byte>> deltas, Span<byte> result)
    {
        // Start with base record
        baseRecord.CopyTo(result);
        
        // Apply each delta in sequence
        foreach (var delta in deltas)
        {
            DeltaCodec.ApplyDelta(result, delta.Span, result);
        }
    }
    
    /// <summary>
    /// Collapses a delta chain into a single full record.
    /// </summary>
    /// <param name="baseRecord">Original full record</param>
    /// <param name="deltas">List of deltas to apply</param>
    /// <returns>Collapsed full record</returns>
    public static byte[] CollapseDeltaChain(ReadOnlySpan<byte> baseRecord, IReadOnlyList<ReadOnlyMemory<byte>> deltas)
    {
        var result = new byte[baseRecord.Length];
        MergeDeltas(baseRecord, deltas, result);
        return result;
    }
}
