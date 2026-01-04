// <copyright file="TableDirectoryManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using SharpCoreDB.DataStructures;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

/// <summary>
/// Manages table metadata storage in single-file (.scdb) databases.
/// Handles table directory block operations and schema persistence.
/// C# 14: Uses modern patterns with spans and zero-allocation parsing.
/// </summary>
internal sealed class TableDirectoryManager : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _tableDirOffset;
    private readonly ulong _tableDirLength;
    private readonly Dictionary<string, TableMetadataEntry> _tableCache;
    private readonly Lock _lock = new();
    private bool _isDirty;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableDirectoryManager"/> class.
    /// </summary>
    /// <param name="provider">The storage provider</param>
    /// <param name="tableDirOffset">Offset to table directory block</param>
    /// <param name="tableDirLength">Length of table directory block</param>
    public TableDirectoryManager(SingleFileStorageProvider provider, ulong tableDirOffset, ulong tableDirLength)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _tableDirOffset = tableDirOffset;
        _tableDirLength = tableDirLength;
        _tableCache = new Dictionary<string, TableMetadataEntry>(StringComparer.OrdinalIgnoreCase);
        
        LoadTableDirectory();
    }

    /// <summary>
    /// Gets all table names in the database.
    /// </summary>
    public IEnumerable<string> GetTableNames()
    {
        lock (_lock)
        {
            return _tableCache.Keys.ToList();
        }
    }

    /// <summary>
    /// Checks if a table exists.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>True if table exists</returns>
    public bool TableExists(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        
        lock (_lock)
        {
            return _tableCache.ContainsKey(tableName);
        }
    }

    /// <summary>
    /// Gets table metadata for the specified table.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>Table metadata or null if not found</returns>
    public TableMetadataEntry? GetTableMetadata(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        
        lock (_lock)
        {
            return _tableCache.TryGetValue(tableName, out var metadata) ? metadata : null;
        }
    }

    /// <summary>
    /// Creates a new table in the directory.
    /// </summary>
    /// <param name="table">Table definition</param>
    /// <param name="dataBlockOffset">Offset to table data block</param>
    /// <param name="columnDefinitions">Column definitions</param>
    /// <param name="indexDefinitions">Index definitions</param>
    public void CreateTable(ITable table, ulong dataBlockOffset, 
        List<ColumnDefinitionEntry> columnDefinitions, List<IndexDefinitionEntry> indexDefinitions)
    {
        ArgumentNullException.ThrowIfNull(table);
        
        var metadata = new TableMetadataEntry
        {
            TableId = GetTableId(table.Name),
            DataBlockOffset = dataBlockOffset,
            PrimaryKeyIndexOffset = 0, // Will be set when PK index is created
            RecordCount = 0,
            ColumnCount = (uint)columnDefinitions.Count,
            PrimaryKeyIndex = table.PrimaryKeyIndex,
            StorageMode = 0, // Default to Columnar
            Flags = (byte)TableFlags.None,
            HashIndexCount = (uint)indexDefinitions.Count(d => d.IndexType == 0),
            BTreeIndexCount = (uint)indexDefinitions.Count(d => d.IndexType == 1),
            ColumnDefsOffset = 0, // Will be set after storing column defs
            IndexDefsOffset = 0,   // Will be set after storing index defs
            CreatedTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ModifiedTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        // Set table name
        SetTableName(ref metadata, table.Name);
        
        // Store column definitions
        metadata.ColumnDefsOffset = StoreColumnDefinitions(table.Name, columnDefinitions);
        
        // Store index definitions
        metadata.IndexDefsOffset = StoreIndexDefinitions(table.Name, indexDefinitions);
        
        lock (_lock)
        {
            _tableCache[table.Name] = metadata;
            _isDirty = true;
        }
    }

    /// <summary>
    /// Updates table metadata (e.g., record count, modification time).
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="updater">Update function</param>
    public void UpdateTableMetadata(string tableName, Func<TableMetadataEntry, TableMetadataEntry> updater)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(updater);
        
        lock (_lock)
        {
            if (_tableCache.TryGetValue(tableName, out var metadata))
            {
                var updated = updater(metadata);
                _tableCache[tableName] = updated;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Deletes a table from the directory.
    /// </summary>
    /// <param name="tableName">Name of the table to delete</param>
    /// <returns>True if table was deleted</returns>
    public bool DeleteTable(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        
        lock (_lock)
        {
            if (_tableCache.Remove(tableName))
            {
                _isDirty = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets column definitions for a table.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>List of column definitions</returns>
    public List<ColumnDefinitionEntry> GetColumnDefinitions(string tableName)
    {
        var metadata = GetTableMetadata(tableName);
        if (metadata == null || metadata.Value.ColumnDefsOffset == 0)
        {
            return new List<ColumnDefinitionEntry>();
        }
        
        return LoadColumnDefinitions(metadata.Value.ColumnDefsOffset, (int)metadata.Value.ColumnCount);
    }

    /// <summary>
    /// Gets index definitions for a table.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>List of index definitions</returns>
    public List<IndexDefinitionEntry> GetIndexDefinitions(string tableName)
    {
        var metadata = GetTableMetadata(tableName);
        if (metadata == null || metadata.Value.IndexDefsOffset == 0)
        {
            return new List<IndexDefinitionEntry>();
        }
        
        var totalIndexes = (int)(metadata.Value.HashIndexCount + metadata.Value.BTreeIndexCount);
        return LoadIndexDefinitions(metadata.Value.IndexDefsOffset, totalIndexes);
    }

    /// <summary>
    /// Flushes table directory changes to disk.
    /// </summary>
    public void Flush()
    {
        if (!_isDirty) return;
        
        lock (_lock)
        {
            if (!_isDirty) return;
            
            SaveTableDirectory();
            _isDirty = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            Flush();
        }
        finally
        {
            _disposed = true;
        }
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private void LoadTableDirectory()
    {
        try
        {
            // Read table directory block
            var blockData = _provider.ReadBlockAsync("sys:tabledir").GetAwaiter().GetResult();
            if (blockData == null || blockData.Length < TableDirectoryHeader.SIZE)
            {
                return; // Empty directory
            }

            var span = blockData.AsSpan();
            
            // Parse header
            var header = MemoryMarshal.Read<TableDirectoryHeader>(span);
            if (!header.IsValid)
            {
                return; // Invalid header
            }

            // Parse table entries
            var offset = TableDirectoryHeader.SIZE;
            for (var i = 0; i < (int)header.TableCount; i++)
            {
                if (offset + TableMetadataEntry.SIZE > span.Length)
                {
                    break; // Corrupted data
                }
                
                var entry = MemoryMarshal.Read<TableMetadataEntry>(span.Slice(offset, TableMetadataEntry.SIZE));
                var tableName = GetTableName(entry);
                
                if (!string.IsNullOrEmpty(tableName))
                {
                    _tableCache[tableName] = entry;
                }
                
                offset += TableMetadataEntry.SIZE;
            }
        }
        catch
        {
            // If loading fails, start with empty directory
            _tableCache.Clear();
        }
    }

    private void SaveTableDirectory()
    {
        var totalSize = TableDirectoryHeader.SIZE + (_tableCache.Count * TableMetadataEntry.SIZE);
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        
        try
        {
            var span = buffer.AsSpan(0, totalSize);
            span.Clear();
            
            // Write header
            var header = new TableDirectoryHeader
            {
                Magic = TableDirectoryHeader.MAGIC,
                Version = TableDirectoryHeader.CURRENT_VERSION,
                TableCount = (uint)_tableCache.Count
            };
            
            MemoryMarshal.Write(span, in header);
            
            // Write table entries
            var offset = TableDirectoryHeader.SIZE;
            foreach (var entry in _tableCache.Values)
            {
                MemoryMarshal.Write(span.Slice(offset, TableMetadataEntry.SIZE), in entry);
                offset += TableMetadataEntry.SIZE;
            }
            
            // Write to block
            _provider.WriteBlockAsync("sys:tabledir", span.ToArray()).GetAwaiter().GetResult();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private ulong StoreColumnDefinitions(string tableName, List<ColumnDefinitionEntry> columns)
    {
        if (columns.Count == 0) return 0;
        
        // Calculate total size
        var totalSize = 0;
        foreach (var col in columns)
        {
            totalSize += ColumnDefinitionEntry.FIXED_SIZE;
            totalSize += (int)col.DefaultValueLength;
            totalSize += (int)col.CheckLength;
        }
        
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        try
        {
            var span = buffer.AsSpan(0, totalSize);
            var offset = 0;
            
            foreach (var col in columns)
            {
                // Write fixed part
                MemoryMarshal.Write(span.Slice(offset, ColumnDefinitionEntry.FIXED_SIZE), in col);
                offset += ColumnDefinitionEntry.FIXED_SIZE;
                
                // Write variable parts (default value and check constraint would go here)
                // For now, just skip as they're not implemented
            }
            
            // Store in block
            var blockName = $"table:{tableName}:columns";
            _provider.WriteBlockAsync(blockName, span.ToArray()).GetAwaiter().GetResult();
            
            // Return offset (simplified - in real implementation would return actual offset)
            return 1; // Placeholder
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private ulong StoreIndexDefinitions(string tableName, List<IndexDefinitionEntry> indexes)
    {
        if (indexes.Count == 0) return 0;
        
        var totalSize = indexes.Count * IndexDefinitionEntry.SIZE;
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
        
        try
        {
            var span = buffer.AsSpan(0, totalSize);
            var offset = 0;
            
            foreach (var index in indexes)
            {
                MemoryMarshal.Write(span.Slice(offset, IndexDefinitionEntry.SIZE), in index);
                offset += IndexDefinitionEntry.SIZE;
            }
            
            // Store in block
            var blockName = $"table:{tableName}:indexes";
            _provider.WriteBlockAsync(blockName, span.ToArray()).GetAwaiter().GetResult();
            
            // Return offset (simplified)
            return 1; // Placeholder
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private List<ColumnDefinitionEntry> LoadColumnDefinitions(ulong offset, int count)
    {
        // Simplified implementation
        return new List<ColumnDefinitionEntry>();
    }

    private List<IndexDefinitionEntry> LoadIndexDefinitions(ulong offset, int count)
    {
        // Simplified implementation
        return new List<IndexDefinitionEntry>();
    }

    private static uint GetTableId(string tableName)
    {
        // Simple hash for table ID
        return (uint)tableName.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe void SetTableName(ref TableMetadataEntry entry, string name)
    {
        if (name.Length > TableMetadataEntry.MAX_TABLE_NAME_LENGTH)
        {
            throw new ArgumentException($"Table name too long: {name.Length} > {TableMetadataEntry.MAX_TABLE_NAME_LENGTH}");
        }
        
        var nameBytes = Encoding.UTF8.GetBytes(name);
        fixed (byte* ptr = entry.TableName)
        {
            var span = new Span<byte>(ptr, TableMetadataEntry.MAX_TABLE_NAME_LENGTH + 1);
            span.Clear();
            nameBytes.CopyTo(span);
        }
    }

    private static unsafe string GetTableName(TableMetadataEntry entry)
    {
        var span = new ReadOnlySpan<byte>(entry.TableName, TableMetadataEntry.MAX_TABLE_NAME_LENGTH + 1);
        var nullIndex = span.IndexOf((byte)0);
        if (nullIndex >= 0)
            span = span[..nullIndex];
        
        return Encoding.UTF8.GetString(span);
    }
}
