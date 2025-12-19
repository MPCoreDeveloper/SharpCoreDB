// <copyright file="Table.Compaction.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Storage.Engines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Table partial class containing compaction operations for columnar (append-only) storage.
/// Compaction removes deleted and stale versions to reclaim disk space.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Checks if compaction is needed based on threshold.
    /// </summary>
    /// <returns>True if compaction threshold reached.</returns>
    public bool NeedsCompaction()
    {
        if (this.StorageMode != SharpCoreDB.Storage.Hybrid.StorageMode.Columnar)
            return false; // Only columnar storage needs compaction
        
        var totalChanges = Interlocked.Read(ref _deletedRowCount) + Interlocked.Read(ref _updatedRowCount);
        return totalChanges >= COMPACTION_THRESHOLD;
    }

    /// <summary>
    /// Triggers automatic compaction in the background if threshold is reached.
    /// </summary>
    public void TryAutoCompact()
    {
        if (NeedsCompaction() && this.StorageMode == SharpCoreDB.Storage.Hybrid.StorageMode.Columnar)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    CompactStorage();
                }
                catch
                {
                    // Silently ignore compaction errors (non-critical background task)
                }
            });
        }
    }

    /// <summary>
    /// Compacts the table storage by removing deleted and stale rows.
    /// Only applicable for columnar (append-only) storage mode.
    /// </summary>
    /// <returns>Compaction statistics (bytes reclaimed, rows removed).</returns>
    public CompactionStats CompactStorage()
    {
        if (this.StorageMode != SharpCoreDB.Storage.Hybrid.StorageMode.Columnar)
        {
            return new CompactionStats { BytesReclaimed = 0, RowsRemoved = 0, Message = "Table is not using columnar storage" };
        }

        rwLock.EnterWriteLock();
        try
        {
            var engine = GetOrCreateStorageEngine();
            
            if (engine is not AppendOnlyEngine appendEngine)
            {
                return new CompactionStats { BytesReclaimed = 0, RowsRemoved = 0, Message = "Storage engine does not support compaction" };
            }

            // Get active positions from primary key index
            var activePositions = new List<long>();
            
            if (PrimaryKeyIndex >= 0)
            {
                // Collect all positions from primary key index
                var pkColumn = Columns[PrimaryKeyIndex];
                var allRows = Select(); // Get all current rows
                
                foreach (var row in allRows)
                {
                    if (row.TryGetValue(pkColumn, out var pkValue) && pkValue != null)
                    {
                        var pkStr = pkValue.ToString() ?? string.Empty;
                        var searchResult = Index.Search(pkStr);
                        if (searchResult.Found)
                        {
                            activePositions.Add(searchResult.Value);
                        }
                    }
                }
            }
            else
            {
                // No primary key - cannot determine active rows
                return new CompactionStats { BytesReclaimed = 0, RowsRemoved = 0, Message = "Table has no primary key - compaction not supported" };
            }

            // Count rows before compaction
            var rowsBeforeCompaction = activePositions.Count;
            
            // Perform compaction
            long bytesReclaimed = appendEngine.CompactTable(Name, activePositions);
            
            // Reset counters
            Interlocked.Exchange(ref _deletedRowCount, 0);
            Interlocked.Exchange(ref _updatedRowCount, 0);
            
            // Rebuild primary key index with new positions
            // Note: After compaction, positions change! We need to rebuild the index.
            RebuildPrimaryKeyIndex();
            
            // Rebuild hash indexes
            foreach (var col in loadedIndexes.ToList())
            {
                RebuildHashIndex(col);
            }
            
            return new CompactionStats
            {
                BytesReclaimed = bytesReclaimed,
                RowsRemoved = 0, // We don't track removed count separately
                Message = $"Compacted {Name}: {bytesReclaimed / 1024.0:F2} KB reclaimed, {rowsBeforeCompaction} active rows"
            };
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Rebuilds the primary key index after compaction.
    /// Positions change after compaction, so we need to rescan the file.
    /// </summary>
    private void RebuildPrimaryKeyIndex()
    {
        if (PrimaryKeyIndex < 0)
            return;
        
        // Clear existing index
        Index = new BTree<string, long>();
        
        // Rescan file to rebuild index
        var engine = GetOrCreateStorageEngine();
        var allRecords = engine.GetAllRecords(Name);
        
        foreach (var (position, data) in allRecords)
        {
            var row = DeserializeRow(data);
            if (row != null && row.TryGetValue(Columns[PrimaryKeyIndex], out var pkValue) && pkValue != null)
            {
                var pkStr = pkValue.ToString() ?? string.Empty;
                Index.Insert(pkStr, position);
            }
        }
    }

    /// <summary>
    /// Rebuilds a hash index after compaction.
    /// </summary>
    /// <param name="columnName">The column to rebuild index for.</param>
    private void RebuildHashIndex(string columnName)
    {
        if (!hashIndexes.TryGetValue(columnName, out var hashIndex))
            return;
        
        // Clear and rebuild
        hashIndex.Clear();
        
        var engine = GetOrCreateStorageEngine();
        var allRecords = engine.GetAllRecords(Name);
        
        foreach (var (position, data) in allRecords)
        {
            var row = DeserializeRow(data);
            if (row != null)
            {
                hashIndex.Add(row, position);
            }
        }
    }
}

/// <summary>
/// Statistics returned by compaction operation.
/// </summary>
public record CompactionStats
{
    /// <summary>Gets the number of bytes reclaimed from disk.</summary>
    public required long BytesReclaimed { get; init; }

    /// <summary>Gets the number of rows removed (deleted + stale versions).</summary>
    public required long RowsRemoved { get; init; }

    /// <summary>Gets a message describing the compaction result.</summary>
    public required string Message { get; init; }
}
