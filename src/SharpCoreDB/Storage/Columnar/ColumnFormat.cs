// <copyright file="ColumnFormat.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Columnar;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Columnar data format specification for efficient analytical queries.
/// C# 14: Record types, primary constructors, collection expressions.
/// 
/// ✅ SCDB Phase 7: Advanced Query Optimization
/// 
/// Format overview:
/// - Column-oriented storage (values grouped by column, not row)
/// - Dictionary encoding for low-cardinality strings
/// - Delta encoding for sorted integer sequences
/// - RLE compression for repeated values
/// - Null bitmap for efficient NULL handling
/// 
/// Benefits:
/// - 50-80% better compression vs row-oriented
/// - Vectorized processing (SIMD-friendly)
/// - Predicate pushdown optimization
/// - Column-wise statistics for query planning
/// </summary>
public sealed record ColumnFormat
{
    /// <summary>Encoding type for column values.</summary>
    public enum ColumnEncoding : byte
    {
        /// <summary>No encoding, raw values.</summary>
        Raw = 0,
        
        /// <summary>Dictionary encoding for low-cardinality strings.</summary>
        Dictionary = 1,
        
        /// <summary>Delta encoding for sorted integers.</summary>
        Delta = 2,
        
        /// <summary>Run-length encoding for repeated values.</summary>
        RunLength = 3,
        
        /// <summary>Frame-of-reference for integer arrays.</summary>
        FrameOfReference = 4,
    }

    /// <summary>Column data type.</summary>
    public enum ColumnType : byte
    {
        /// <summary>8-bit signed integer.</summary>
        Int8 = 1,
        
        /// <summary>16-bit signed integer.</summary>
        Int16 = 2,
        
        /// <summary>32-bit signed integer.</summary>
        Int32 = 4,
        
        /// <summary>64-bit signed integer.</summary>
        Int64 = 8,
        
        /// <summary>32-bit floating point.</summary>
        Float32 = 11,
        
        /// <summary>64-bit floating point.</summary>
        Float64 = 12,
        
        /// <summary>UTF-8 encoded string.</summary>
        String = 20,
        
        /// <summary>Binary data (BLOB).</summary>
        Binary = 21,
        
        /// <summary>Boolean value.</summary>
        Boolean = 30,
        
        /// <summary>DateTime value (UTC tick count).</summary>
        DateTime = 31,
        
        /// <summary>GUID (128-bit).</summary>
        Guid = 32,
    }

    /// <summary>Column metadata and encoding information.</summary>
    public sealed record ColumnMetadata
    {
        /// <summary>Column name.</summary>
        public required string ColumnName { get; init; }
        
        /// <summary>Column ordinal position.</summary>
        public required int ColumnOrdinal { get; init; }
        
        /// <summary>Data type of column values.</summary>
        public required ColumnType DataType { get; init; }
        
        /// <summary>Encoding applied to this column.</summary>
        public required ColumnEncoding Encoding { get; init; }
        
        /// <summary>Number of values in column (including NULLs).</summary>
        public required int ValueCount { get; init; }
        
        /// <summary>Number of NULL values.</summary>
        public required int NullCount { get; init; }
        
        /// <summary>Minimum value (for numeric types).</summary>
        public object? MinValue { get; init; }
        
        /// <summary>Maximum value (for numeric types).</summary>
        public object? MaxValue { get; init; }
        
        /// <summary>Cardinality (distinct value count).</summary>
        public int Cardinality { get; init; }
        
        /// <summary>Size of encoded data in bytes.</summary>
        public long EncodedSize { get; init; }
        
        /// <summary>Original uncompressed size in bytes.</summary>
        public long UncompressedSize { get; init; }

        /// <summary>Gets compression ratio (0.0 - 1.0).</summary>
        public double CompressionRatio => UncompressedSize > 0 
            ? (double)EncodedSize / UncompressedSize 
            : 1.0;
    }

    /// <summary>Null bitmap for efficient NULL representation.</summary>
    public sealed class NullBitmap
    {
        private readonly byte[] bitmap;
        private readonly int valueCount;

        /// <summary>Initializes a new null bitmap for given value count.</summary>
        public NullBitmap(int valueCount)
        {
            this.valueCount = valueCount;
            this.bitmap = new byte[(valueCount + 7) / 8];
        }

        /// <summary>Marks value at index as NULL.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void SetNull(int index)
        {
            if (index < 0 || index >= valueCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            
            var byteIndex = index / 8;
            var bitIndex = index % 8;
            bitmap[byteIndex] |= (byte)(1 << bitIndex);
        }

        /// <summary>Checks if value at index is NULL.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public bool IsNull(int index)
        {
            if (index < 0 || index >= valueCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            
            var byteIndex = index / 8;
            var bitIndex = index % 8;
            return (bitmap[byteIndex] & (1 << bitIndex)) != 0;
        }

        /// <summary>Gets underlying bitmap bytes.</summary>
        public ReadOnlySpan<byte> GetBytes() => bitmap.AsSpan();

        /// <summary>Gets size of bitmap in bytes.</summary>
        public int SizeBytes => bitmap.Length;
    }

    /// <summary>Dictionary for encoded string values.</summary>
    public sealed class StringDictionary
    {
        private readonly List<string> entries = [];
        private readonly Dictionary<string, int> indexMap = [];

        /// <summary>Gets or creates index for string value.</summary>
        public int GetOrAddIndex(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            
            if (indexMap.TryGetValue(value, out var index))
                return index;
            
            index = entries.Count;
            entries.Add(value);
            indexMap[value] = index;
            return index;
        }

        /// <summary>Gets string at index.</summary>
        public string GetString(int index)
        {
            if (index < 0 || index >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            
            return entries[index];
        }

        /// <summary>Gets all dictionary entries.</summary>
        public IReadOnlyList<string> Entries => entries.AsReadOnly();

        /// <summary>Gets dictionary size.</summary>
        public int Count => entries.Count;
    }

    /// <summary>Gets column count.</summary>
    public int ColumnCount => Columns.Count;

    /// <summary>Column definitions in order.</summary>
    public required List<ColumnMetadata> Columns { get; init; }

    /// <summary>Total row count (excluding header).</summary>
    public required int RowCount { get; init; }

    /// <summary>Format version for compatibility.</summary>
    public byte FormatVersion => 1;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Validates format correctness.</summary>
    public bool Validate()
    {
        if (Columns.Count == 0)
            return false;

        if (RowCount < 0)
            return false;

        var ordinalSet = new HashSet<int>();
        foreach (var col in Columns)
        {
            if (string.IsNullOrEmpty(col.ColumnName))
                return false;
            
            if (col.ColumnOrdinal < 0 || col.ColumnOrdinal >= Columns.Count)
                return false;
            
            if (!ordinalSet.Add(col.ColumnOrdinal))
                return false; // Duplicate ordinal
            
            if (col.ValueCount != RowCount)
                return false; // Inconsistent row count
            
            if (col.NullCount < 0 || col.NullCount > col.ValueCount)
                return false;
        }

        return true;
    }
}
