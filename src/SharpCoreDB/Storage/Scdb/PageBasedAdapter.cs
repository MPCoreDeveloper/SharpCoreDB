// <copyright file="PageBasedAdapter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Interfaces;

/// <summary>
/// Adapter that integrates PageBasedEngine with SCDB SingleFileStorageProvider.
/// Routes page operations through the unified storage layer for persistence.
/// C# 14: Uses Lock type and modern async patterns.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 4: Provides seamless integration between page-based storage
/// and the SCDB single-file format, enabling:
/// - Atomic page writes via WAL
/// - Memory-mapped reads for performance
/// - Unified storage format across all data
/// </remarks>
public sealed class PageBasedAdapter : IStorageEngine, IDisposable
{
    private readonly SingleFileStorageProvider _storageProvider;
    private readonly Lock _adapterLock = new();
    private readonly Dictionary<string, TablePageInfo> _tableInfo = [];
    private bool _isInTransaction;
    private bool _disposed;

    // Block name prefixes for SCDB storage
    private const string PAGE_PREFIX = "page:";
    private const string META_PREFIX = "pagemeta:";
    private const string FREE_PREFIX = "pagefree:";

    // Performance metrics
    private long _totalInserts;
    private long _totalUpdates;
    private long _totalDeletes;
    private long _totalReads;
    private long _bytesWritten;
    private long _bytesRead;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageBasedAdapter"/> class.
    /// </summary>
    /// <param name="storageProvider">The SCDB storage provider to use.</param>
    public PageBasedAdapter(SingleFileStorageProvider storageProvider)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        _storageProvider = storageProvider;
    }

    /// <summary>
    /// Creates a PageBasedAdapter from an existing SCDB file.
    /// </summary>
    /// <param name="scdbPath">Path to the .scdb file.</param>
    /// <param name="options">Database options.</param>
    /// <returns>Initialized adapter.</returns>
    public static PageBasedAdapter Open(string scdbPath, DatabaseOptions options)
    {
        var provider = SingleFileStorageProvider.Open(scdbPath, options);
        return new PageBasedAdapter(provider);
    }

    /// <inheritdoc/>
    public StorageEngineType EngineType => StorageEngineType.PageBased;

    /// <inheritdoc/>
    public bool IsInTransaction => _isInTransaction;

    /// <inheritdoc/>
    public long Insert(string tableName, byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        
        lock (_adapterLock)
        {
            var tableInfo = GetOrCreateTableInfo(tableName);
            
            // Find a page with enough space or create new one
            var pageId = FindOrCreatePage(tableName, tableInfo, data.Length);
            
            // Insert record into page
            var slotIndex = InsertRecordIntoPage(tableName, pageId, data);
            
            // Update metrics
            Interlocked.Increment(ref _totalInserts);
            Interlocked.Add(ref _bytesWritten, data.Length);
            
            // Return encoded storage reference (pageId + slotIndex)
            return EncodeStorageReference(pageId, slotIndex);
        }
    }

    /// <inheritdoc/>
    public long[] InsertBatch(string tableName, List<byte[]> dataBlocks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(dataBlocks);
        
        if (dataBlocks.Count == 0)
        {
            return [];
        }
        
        lock (_adapterLock)
        {
            var results = new long[dataBlocks.Count];
            var tableInfo = GetOrCreateTableInfo(tableName);
            
            for (int i = 0; i < dataBlocks.Count; i++)
            {
                var data = dataBlocks[i];
                var pageId = FindOrCreatePage(tableName, tableInfo, data.Length);
                var slotIndex = InsertRecordIntoPage(tableName, pageId, data);
                results[i] = EncodeStorageReference(pageId, slotIndex);
                
                Interlocked.Add(ref _bytesWritten, data.Length);
            }
            
            Interlocked.Add(ref _totalInserts, dataBlocks.Count);
            return results;
        }
    }

    /// <inheritdoc/>
    public byte[]? Read(string tableName, long storageReference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var (pageId, slotIndex) = DecodeStorageReference(storageReference);
        var blockName = GetPageBlockName(tableName, pageId);
        
        // Read page data
        var pageData = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
        if (pageData == null)
        {
            return null;
        }
        
        // Extract record from slot
        var record = ExtractRecordFromPage(pageData, slotIndex);
        
        if (record != null)
        {
            Interlocked.Increment(ref _totalReads);
            Interlocked.Add(ref _bytesRead, record.Length);
        }
        
        return record;
    }

    /// <inheritdoc/>
    public void Update(string tableName, long storageReference, byte[] newData)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(newData);
        
        lock (_adapterLock)
        {
            var (pageId, slotIndex) = DecodeStorageReference(storageReference);
            var blockName = GetPageBlockName(tableName, pageId);
            
            // Read existing page
            var pageData = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            if (pageData == null)
            {
                throw new InvalidOperationException($"Page {pageId} not found for table {tableName}");
            }
            
            // Update record in page
            var updatedPage = UpdateRecordInPage(pageData, slotIndex, newData);
            
            // Write back
            _storageProvider.WriteBlockAsync(blockName, updatedPage).GetAwaiter().GetResult();
            
            Interlocked.Increment(ref _totalUpdates);
            Interlocked.Add(ref _bytesWritten, newData.Length);
        }
    }

    /// <inheritdoc/>
    public void Delete(string tableName, long storageReference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        lock (_adapterLock)
        {
            var (pageId, slotIndex) = DecodeStorageReference(storageReference);
            var blockName = GetPageBlockName(tableName, pageId);
            
            // Read existing page
            var pageData = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            if (pageData == null)
            {
                return; // Already deleted or doesn't exist
            }
            
            // Mark record as deleted in page
            var updatedPage = DeleteRecordInPage(pageData, slotIndex);
            
            // Write back
            _storageProvider.WriteBlockAsync(blockName, updatedPage).GetAwaiter().GetResult();
            
            Interlocked.Increment(ref _totalDeletes);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<(long storageReference, byte[] data)> GetAllRecords(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var tableInfo = GetTableInfo(tableName);
        if (tableInfo == null)
        {
            yield break;
        }
        
        // Iterate all pages for this table
        for (uint pageId = 0; pageId < tableInfo.NextPageId; pageId++)
        {
            var blockName = GetPageBlockName(tableName, pageId);
            var pageData = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
            
            if (pageData == null)
            {
                continue;
            }
            
            // Extract all records from page
            foreach (var (slotIndex, data) in ExtractAllRecordsFromPage(pageData))
            {
                yield return (EncodeStorageReference(pageId, slotIndex), data);
            }
        }
    }

    /// <inheritdoc/>
    public void BeginTransaction()
    {
        lock (_adapterLock)
        {
            if (_isInTransaction)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }
            
            _storageProvider.BeginTransaction();
            _isInTransaction = true;
        }
    }

    /// <inheritdoc/>
    public async Task CommitAsync()
    {
        await Task.Run(() =>
        {
            lock (_adapterLock)
            {
                if (!_isInTransaction)
                {
                    throw new InvalidOperationException("No transaction in progress");
                }
                
                _storageProvider.CommitTransactionAsync().GetAwaiter().GetResult();
                _isInTransaction = false;
            }
        });
    }

    /// <inheritdoc/>
    public void Rollback()
    {
        lock (_adapterLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No transaction in progress");
            }
            
            _storageProvider.RollbackTransaction();
            _isInTransaction = false;
        }
    }

    /// <inheritdoc/>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _storageProvider.FlushAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public StorageEngineMetrics GetMetrics()
    {
        return new StorageEngineMetrics
        {
            TotalInserts = _totalInserts,
            TotalUpdates = _totalUpdates,
            TotalDeletes = _totalDeletes,
            TotalReads = _totalReads,
            BytesWritten = _bytesWritten,
            BytesRead = _bytesRead,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_adapterLock)
        {
            if (_isInTransaction)
            {
                _storageProvider.RollbackTransaction();
            }
            
            // Save table metadata before disposing
            SaveTableMetadata();
            
            _disposed = true;
        }
        
        // Note: We don't dispose _storageProvider as it may be shared
    }

    // ========================================
    // Private Implementation
    // ========================================

    private TablePageInfo GetOrCreateTableInfo(string tableName)
    {
        if (!_tableInfo.TryGetValue(tableName, out var info))
        {
            info = LoadOrCreateTableInfo(tableName);
            _tableInfo[tableName] = info;
        }
        
        return info;
    }

    private TablePageInfo? GetTableInfo(string tableName)
    {
        if (_tableInfo.TryGetValue(tableName, out var info))
        {
            return info;
        }
        
        return LoadTableInfo(tableName);
    }

    private TablePageInfo LoadOrCreateTableInfo(string tableName)
    {
        var existing = LoadTableInfo(tableName);
        if (existing != null)
        {
            return existing;
        }
        
        return new TablePageInfo
        {
            TableName = tableName,
            NextPageId = 0,
            PageSize = _storageProvider.PageSize > 0 ? _storageProvider.PageSize : 8192,
            FreePageIds = [],
        };
    }

    private TablePageInfo? LoadTableInfo(string tableName)
    {
        var metaBlockName = $"{META_PREFIX}{tableName}";
        var data = _storageProvider.ReadBlockAsync(metaBlockName).GetAwaiter().GetResult();
        
        if (data == null || data.Length < 16)
        {
            return null;
        }
        
        return new TablePageInfo
        {
            TableName = tableName,
            NextPageId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)),
            PageSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4)),
            FreePageIds = [],  // TODO: Load from FREE_PREFIX block
        };
    }

    private void SaveTableMetadata()
    {
        foreach (var (tableName, info) in _tableInfo)
        {
            var metaBlockName = $"{META_PREFIX}{tableName}";
            var buffer = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), info.NextPageId);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), info.PageSize);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), 0); // Reserved
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12, 4), 0); // Reserved
                
                _storageProvider.WriteBlockAsync(metaBlockName, buffer.AsMemory(0, 16)).GetAwaiter().GetResult();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private uint FindOrCreatePage(string tableName, TablePageInfo tableInfo, int requiredSpace)
    {
        // For simplicity, always create new page if current is full
        // TODO: Implement proper page space tracking and reuse
        
        var pageId = tableInfo.NextPageId;
        tableInfo.NextPageId++;
        
        // Initialize new page
        var pageData = new byte[tableInfo.PageSize];
        InitializePage(pageData, pageId);
        
        var blockName = GetPageBlockName(tableName, pageId);
        _storageProvider.WriteBlockAsync(blockName, pageData).GetAwaiter().GetResult();
        
        return pageId;
    }

    private ushort InsertRecordIntoPage(string tableName, uint pageId, byte[] data)
    {
        var blockName = GetPageBlockName(tableName, pageId);
        var pageData = _storageProvider.ReadBlockAsync(blockName).GetAwaiter().GetResult();
        
        if (pageData == null)
        {
            throw new InvalidOperationException($"Page {pageId} not found");
        }
        
        // Simple slot-based insertion
        // Page format:
        // [Header 16 bytes] [Slot Directory] [Free Space] [Records (from end)]
        
        var header = ReadPageHeader(pageData);
        var slotIndex = header.SlotCount;
        
        // Calculate record position (from end of page)
        var recordEnd = header.FreeSpaceEnd;
        var recordStart = recordEnd - data.Length;
        
        if (recordStart < header.FreeSpaceStart + 4) // 4 bytes for new slot entry
        {
            throw new InvalidOperationException("Page full");
        }
        
        // Write record data
        data.CopyTo(pageData.AsSpan(recordStart));
        
        // Write slot entry (offset + length)
        var slotOffset = 16 + (slotIndex * 4);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset), (ushort)recordStart);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset + 2), (ushort)data.Length);
        
        // Update header
        header.SlotCount++;
        header.FreeSpaceStart = (ushort)(slotOffset + 4);
        header.FreeSpaceEnd = (ushort)recordStart;
        WritePageHeader(pageData, header);
        
        // Write back to storage
        _storageProvider.WriteBlockAsync(blockName, pageData).GetAwaiter().GetResult();
        
        return slotIndex;
    }

    private byte[]? ExtractRecordFromPage(byte[] pageData, ushort slotIndex)
    {
        var header = ReadPageHeader(pageData);
        
        if (slotIndex >= header.SlotCount)
        {
            return null;
        }
        
        var slotOffset = 16 + (slotIndex * 4);
        var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(pageData.AsSpan(slotOffset));
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(pageData.AsSpan(slotOffset + 2));
        
        if (recordOffset == 0 && recordLength == 0)
        {
            return null; // Deleted
        }
        
        return pageData.AsSpan(recordOffset, recordLength).ToArray();
    }

    private IEnumerable<(ushort slotIndex, byte[] data)> ExtractAllRecordsFromPage(byte[] pageData)
    {
        var header = ReadPageHeader(pageData);
        
        for (ushort i = 0; i < header.SlotCount; i++)
        {
            var record = ExtractRecordFromPage(pageData, i);
            if (record != null)
            {
                yield return (i, record);
            }
        }
    }

    private byte[] UpdateRecordInPage(byte[] pageData, ushort slotIndex, byte[] newData)
    {
        var header = ReadPageHeader(pageData);
        
        if (slotIndex >= header.SlotCount)
        {
            throw new InvalidOperationException($"Slot {slotIndex} not found");
        }
        
        // For simplicity, mark old slot as deleted and insert at new position
        // TODO: In-place update if new data fits
        
        var slotOffset = 16 + (slotIndex * 4);
        var recordEnd = header.FreeSpaceEnd;
        var recordStart = recordEnd - newData.Length;
        
        // Write new record
        newData.CopyTo(pageData.AsSpan(recordStart));
        
        // Update slot entry
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset), (ushort)recordStart);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset + 2), (ushort)newData.Length);
        
        // Update header
        header.FreeSpaceEnd = (ushort)recordStart;
        WritePageHeader(pageData, header);
        
        return pageData;
    }

    private byte[] DeleteRecordInPage(byte[] pageData, ushort slotIndex)
    {
        var header = ReadPageHeader(pageData);
        
        if (slotIndex >= header.SlotCount)
        {
            return pageData;
        }
        
        // Mark slot as deleted (zero offset and length)
        var slotOffset = 16 + (slotIndex * 4);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(slotOffset + 2), 0);
        
        return pageData;
    }

    private static void InitializePage(byte[] pageData, uint pageId)
    {
        var header = new PageHeader
        {
            Magic = 0x50414745, // "PAGE"
            PageId = pageId,
            SlotCount = 0,
            FreeSpaceStart = 16, // After header
            FreeSpaceEnd = (ushort)pageData.Length,
        };
        
        WritePageHeader(pageData, header);
    }

    private static PageHeader ReadPageHeader(byte[] pageData)
    {
        return new PageHeader
        {
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(pageData.AsSpan(0, 4)),
            PageId = BinaryPrimitives.ReadUInt32LittleEndian(pageData.AsSpan(4, 4)),
            SlotCount = BinaryPrimitives.ReadUInt16LittleEndian(pageData.AsSpan(8, 2)),
            FreeSpaceStart = BinaryPrimitives.ReadUInt16LittleEndian(pageData.AsSpan(10, 2)),
            FreeSpaceEnd = BinaryPrimitives.ReadUInt16LittleEndian(pageData.AsSpan(12, 2)),
        };
    }

    private static void WritePageHeader(byte[] pageData, PageHeader header)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(pageData.AsSpan(0, 4), header.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(pageData.AsSpan(4, 4), header.PageId);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(8, 2), header.SlotCount);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(10, 2), header.FreeSpaceStart);
        BinaryPrimitives.WriteUInt16LittleEndian(pageData.AsSpan(12, 2), header.FreeSpaceEnd);
    }

    private static string GetPageBlockName(string tableName, uint pageId) 
        => $"{PAGE_PREFIX}{tableName}:{pageId}";

    private static long EncodeStorageReference(uint pageId, ushort slotIndex)
        => ((long)pageId << 16) | slotIndex;

    private static (uint pageId, ushort slotIndex) DecodeStorageReference(long reference)
        => ((uint)(reference >> 16), (ushort)(reference & 0xFFFF));

    // ========================================
    // Internal Types
    // ========================================

    private sealed class TablePageInfo
    {
        public required string TableName { get; init; }
        public uint NextPageId { get; set; }
        public int PageSize { get; init; }
        public List<uint> FreePageIds { get; init; } = [];
    }

    private struct PageHeader
    {
        public uint Magic;
        public uint PageId;
        public ushort SlotCount;
        public ushort FreeSpaceStart;
        public ushort FreeSpaceEnd;
    }
}
