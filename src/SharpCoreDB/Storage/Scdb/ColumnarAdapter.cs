// <copyright file="ColumnarAdapter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.ColumnStorage;

/// <summary>
/// Adapter for columnar storage integration with SCDB SingleFileStorageProvider.
/// Stores columns as separate blocks for efficient analytics queries.
/// C# 14: Uses modern async patterns and Brotli compression.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 4: Enables ColumnStore to persist to SCDB format.
/// Each column stored as separate block for:
/// - Efficient column-based reads (only fetch needed columns)
/// - Better compression (similar values compress well)
/// - SIMD-friendly memory layout
/// </remarks>
/// <typeparam name="T">The entity type stored in columnar format.</typeparam>
public sealed class ColumnarAdapter<T> : IDisposable where T : class, new()
{
    private readonly SingleFileStorageProvider _storageProvider;
    private readonly string _tableName;
    private readonly Lock _adapterLock = new();
    private ColumnStore<T>? _columnStore;
    private bool _disposed;
    private List<string> _metadataColumnNames = [];

    // Block name prefixes
    private const string COLUMN_PREFIX = "column:";
    private const string META_PREFIX = "colmeta:";
    private const string STATS_PREFIX = "colstats:";

    // Column header magic
    private const uint COLUMN_MAGIC = 0x434F4C31; // "COL1"

    /// <summary>
    /// Initializes a new instance of the <see cref="ColumnarAdapter{T}"/> class.
    /// </summary>
    /// <param name="storageProvider">The SCDB storage provider.</param>
    /// <param name="tableName">The table name for block naming.</param>
    public ColumnarAdapter(SingleFileStorageProvider storageProvider, string tableName)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        
        _storageProvider = storageProvider;
        _tableName = tableName;
    }

    /// <summary>
    /// Gets or creates the underlying ColumnStore.
    /// </summary>
    public ColumnStore<T> ColumnStore
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _columnStore ??= new ColumnStore<T>();
        }
    }

    /// <summary>
    /// Gets the number of rows stored.
    /// </summary>
    public int RowCount => _columnStore?.RowCount ?? 0;

    /// <summary>
    /// Transposes row data to columnar format and persists to SCDB.
    /// </summary>
    /// <param name="rows">The rows to transpose and persist.</param>
    /// <param name="compress">Whether to compress column data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task TransposeAndPersistAsync(
        IEnumerable<T> rows, 
        bool compress = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rows);
        
        lock (_adapterLock)
        {
            // Create/reset column store
            _columnStore?.Dispose();
            _columnStore = new ColumnStore<T>();
            
            // Transpose rows to columns
            _columnStore.Transpose(rows);
        }
        
        // Persist each column to SCDB
        await PersistColumnsAsync(compress, cancellationToken);
        
        // Persist metadata
        await PersistMetadataAsync(cancellationToken);
    }

    /// <summary>
    /// Loads columns from SCDB storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if columns were loaded, false if not found.</returns>
    public async Task<bool> LoadColumnsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Read metadata first
        var metaBlockName = $"{META_PREFIX}{_tableName}";
        var metaData = await _storageProvider.ReadBlockAsync(metaBlockName, cancellationToken);
        
        if (metaData == null || metaData.Length < 16)
        {
            return false;
        }
        
        var rowCount = BinaryPrimitives.ReadInt32LittleEndian(metaData.AsSpan(0, 4));
        var columnCount = BinaryPrimitives.ReadInt32LittleEndian(metaData.AsSpan(4, 4));
        
        if (rowCount == 0 || columnCount == 0)
        {
            return false;
        }

        _metadataColumnNames.Clear();

        // Extended metadata (v2): header 16 + [nameLen:int + utf8 bytes]*N
        if (metaData.Length > 16)
        {
            var offset = 16;
            for (var i = 0; i < columnCount && offset + 4 <= metaData.Length; i++)
            {
                var len = BinaryPrimitives.ReadInt32LittleEndian(metaData.AsSpan(offset, 4));
                offset += 4;
                if (len <= 0 || offset + len > metaData.Length)
                {
                    break;
                }

                var name = Encoding.UTF8.GetString(metaData.AsSpan(offset, len));
                _metadataColumnNames.Add(name);
                offset += len;
            }
        }

        // Backward compatibility: if names are not present, infer from model properties.
        if (_metadataColumnNames.Count == 0)
        {
            _metadataColumnNames = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToList();
        }

        return _metadataColumnNames.Count > 0;
    }

    /// <summary>
    /// Gets a typed column buffer for fast access and aggregates.
    /// </summary>
    /// <typeparam name="TValue">The column value type.</typeparam>
    /// <param name="columnName">The column name.</param>
    /// <returns>The typed column buffer.</returns>
    public ColumnBuffer<TValue> GetColumn<TValue>(string columnName) where TValue : struct
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (_columnStore == null)
        {
            throw new InvalidOperationException("No data loaded. Call TransposeAndPersistAsync first.");
        }
        
        return _columnStore.GetColumn<TValue>(columnName);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        _columnStore?.Dispose();
        _disposed = true;
    }

    // ========================================
    // Private Implementation
    // ========================================

    private async Task PersistColumnsAsync(bool compress, CancellationToken cancellationToken)
    {
        if (_columnStore == null) return;
        
        foreach (var columnName in _columnStore.ColumnNames)
        {
            var blockName = GetColumnBlockName(columnName);
            var columnData = SerializeColumn(columnName, compress);
            
            await _storageProvider.WriteBlockAsync(blockName, columnData, cancellationToken);
        }
    }

    private async Task PersistMetadataAsync(CancellationToken cancellationToken)
    {
        if (_columnStore == null) return;
        
        var metaBlockName = $"{META_PREFIX}{_tableName}";

        // Extended metadata format:
        // rowCount (4) + columnCount (4) + timestamp (8) + [nameLen (4) + utf8 name]*N
        var encodedNames = _columnStore.ColumnNames
            .Select(n => Encoding.UTF8.GetBytes(n))
            .ToList();

        var namesBytesLength = encodedNames.Sum(n => 4 + n.Length);
        var totalLength = 16 + namesBytesLength;

        var buffer = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), _columnStore.RowCount);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), _columnStore.ColumnNames.Count);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(8, 8), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            var offset = 16;
            foreach (var nameBytes in encodedNames)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), nameBytes.Length);
                offset += 4;
                nameBytes.CopyTo(buffer.AsSpan(offset, nameBytes.Length));
                offset += nameBytes.Length;
            }
            
            await _storageProvider.WriteBlockAsync(metaBlockName, buffer.AsMemory(0, totalLength), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private byte[] SerializeColumn(string columnName, bool compress)
    {
        // Get raw column data
        var rawData = GetRawColumnData(columnName);
        
        if (!compress || rawData.Length < 100)
        {
            // Don't compress small columns
            return CreateColumnBlock(rawData, compressed: false);
        }
        
        // Compress with Brotli
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest))
        {
            brotli.Write(rawData);
        }
        
        var compressedData = output.ToArray();
        
        // Only use compression if it actually saves space
        if (compressedData.Length < rawData.Length * 0.9)
        {
            return CreateColumnBlock(compressedData, compressed: true);
        }
        
        return CreateColumnBlock(rawData, compressed: false);
    }

    private byte[] GetRawColumnData(string columnName)
    {
        if (_columnStore == null)
        {
            return [];
        }
        
        // Get column as span and convert to bytes
        // This is a simplified implementation - in production, use proper type handling
        var columnType = GetColumnType(columnName);
        
        return columnType.Name switch
        {
            "Int32" => GetInt32ColumnBytes(columnName),
            "Int64" => GetInt64ColumnBytes(columnName),
            "Double" => GetDoubleColumnBytes(columnName),
            "Decimal" => GetDecimalColumnBytes(columnName),
            _ => []
        };
    }

    private static Type GetColumnType(string columnName)
    {
        var prop = typeof(T).GetProperty(columnName);
        return prop?.PropertyType ?? typeof(object);
    }

    private byte[] GetInt32ColumnBytes(string columnName)
    {
        var column = _columnStore.GetColumn<int>(columnName);
        var data = column.GetData();
        var bytes = new byte[data.Length * sizeof(int)];
        
        for (int i = 0; i < data.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4, 4), data[i]);
        }
        
        return bytes;
    }

    private byte[] GetInt64ColumnBytes(string columnName)
    {
        var column = _columnStore.GetColumn<long>(columnName);
        var data = column.GetData();
        var bytes = new byte[data.Length * sizeof(long)];
        
        for (int i = 0; i < data.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8, 8), data[i]);
        }
        
        return bytes;
    }

    private byte[] GetDoubleColumnBytes(string columnName)
    {
        var column = _columnStore.GetColumn<double>(columnName);
        var data = column.GetData();
        var bytes = new byte[data.Length * sizeof(double)];
        
        for (int i = 0; i < data.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8, 8), BitConverter.DoubleToInt64Bits(data[i]));
        }
        
        return bytes;
    }

    private byte[] GetDecimalColumnBytes(string columnName)
    {
        var column = _columnStore.GetColumn<decimal>(columnName);
        var data = column.GetData();
        var bytes = new byte[data.Length * 16];

        for (int i = 0; i < data.Length; i++)
        {
            var bits = decimal.GetBits(data[i]);
            var offset = i * 16;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), bits[0]);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4, 4), bits[1]);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8, 4), bits[2]);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 12, 4), bits[3]);
        }

        return bytes;
    }

    private static byte[] CreateColumnBlock(byte[] data, bool compressed)
    {
        // Column Block Format:
        // [Magic 4] [Flags 1] [Reserved 3] [UncompressedSize 4] [DataSize 4] [Data...]
        
        var header = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), COLUMN_MAGIC);
        header[4] = (byte)(compressed ? 1 : 0);
        // bytes 5-7: reserved
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), data.Length);
        
        var result = new byte[16 + data.Length];
        header.CopyTo(result, 0);
        data.CopyTo(result, 16);
        
        return result;
    }

    private string GetColumnBlockName(string columnName) 
        => $"{COLUMN_PREFIX}{_tableName}:{columnName}";
}
