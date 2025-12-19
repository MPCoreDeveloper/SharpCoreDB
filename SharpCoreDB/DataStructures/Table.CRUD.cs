namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Buffers;
using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// CRUD operations for Table - Insert, Select, Update, Delete.
/// Now includes hybrid storage support with PageManager integration.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Inserts a row into the table.
    /// Routes to columnar or page-based storage ENGINE based on StorageMode.
    /// </summary>
    /// <param name="row">The row data to insert.</param>
    /// <exception cref="ArgumentNullException">Thrown when storage is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when table is readonly or primary key violation occurs.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Insert(Dictionary<string, object> row)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        if (this.isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            // Validate + fill defaults
            for (int i = 0; i < this.Columns.Count; i++)
            {
                var col = this.Columns[i];
                if (!row.TryGetValue(col, out var val))
                {
                    row[col] = this.IsAuto[i] ? GenerateAutoValue(this.ColumnTypes[i]) : GetDefaultValue(this.ColumnTypes[i]) ?? DBNull.Value;
                }
                else if (val != DBNull.Value && !IsValidType(val, this.ColumnTypes[i]))
                {
                    throw new InvalidOperationException($"Type mismatch for column {col}");
                }
            }

            // Primary key check
            if (this.PrimaryKeyIndex >= 0)
            {
                var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                if (this.Index.Search(pkVal).Found)
                    throw new InvalidOperationException("Primary key violation");
            }

            // ✅ NEW: Route through storage engine
            var engine = GetOrCreateStorageEngine();
            
            // Serialize row data
            int estimatedSize = EstimateRowSize(row);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
            try
            {
                if (SimdHelper.IsSimdSupported)
                {
                    SimdHelper.ZeroBuffer(buffer.AsSpan(0, estimatedSize));
                }
                else
                {
                    Array.Clear(buffer, 0, estimatedSize);
                }

                int bytesWritten = 0;
                Span<byte> bufferSpan = buffer.AsSpan();
                
                foreach (var col in this.Columns)
                {
                    int written = WriteTypedValueToSpan(bufferSpan.Slice(bytesWritten), row[col], this.ColumnTypes[this.Columns.IndexOf(col)]);
                    bytesWritten += written;
                }

                var rowData = buffer.AsSpan(0, bytesWritten).ToArray();

                // ✅ ROUTE TO ENGINE: Single Insert() call
                long position = engine.Insert(Name, rowData);

                // Update indexes
                if (this.PrimaryKeyIndex >= 0)
                {
                    var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                    this.Index.Insert(pkVal, position);
                }

                // Hash indexes (only for columnar mode)
                if (StorageMode == StorageMode.Columnar)
                {
                    var unloadedIndexes = this.registeredIndexes.Keys.Where(col => !this.loadedIndexes.Contains(col)).ToList();
                    foreach (var registeredCol in unloadedIndexes)
                    {
                        EnsureIndexLoaded(registeredCol);
                    }

                    foreach (var hashIndex in this.hashIndexes.Values)
                    {
                        hashIndex.Add(row, position);
                    }
                    
                    foreach (var registeredCol in this.registeredIndexes.Keys.Where(col => !this.loadedIndexes.Contains(col)).ToList())
                    {
                        this.staleIndexes.Add(registeredCol);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Inserts multiple rows in a single batch operation.
    /// Routes to columnar or page-based storage ENGINE based on StorageMode.
    /// ✅ CRITICAL: Uses engine transaction for batching!
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long[] InsertBatch(List<Dictionary<string, object>> rows)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        ArgumentNullException.ThrowIfNull(rows);
        
        if (rows.Count == 0) return [];
        if (this.isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            // ✅ CRITICAL FIX: Start engine transaction for batching!
            var engine = GetOrCreateStorageEngine();
            bool needsTransaction = !engine.IsInTransaction;
            
            if (needsTransaction)
            {
                engine.BeginTransaction();
            }
            
            try
            {
                // Step 1: Validate all rows and fill defaults
                for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
                {
                    var row = rows[rowIdx];
                    
                    for (int i = 0; i < this.Columns.Count; i++)
                    {
                        var col = this.Columns[i];
                        if (!row.TryGetValue(col, out var val))
                        {
                            row[col] = this.IsAuto[i] ? GenerateAutoValue(this.ColumnTypes[i]) : GetDefaultValue(this.ColumnTypes[i]) ?? DBNull.Value;
                        }
                        else if (val != DBNull.Value && val is not null && !IsValidType(val, this.ColumnTypes[i]))
                        {
                            throw new InvalidOperationException($"Type mismatch for column {col} in row {rowIdx}");
                        }
                    }

                    if (this.PrimaryKeyIndex >= 0)
                    {
                        var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                        if (this.Index.Search(pkVal).Found)
                            throw new InvalidOperationException($"Primary key violation in row {rowIdx}: {pkVal}");
                    }
                }

                // Step 2: Serialize all rows
                var serializedRows = new List<byte[]>(rows.Count);
                
                foreach (var row in rows)
                {
                    int estimatedSize = EstimateRowSize(row);
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
                    
                    try
                    {
                        if (SimdHelper.IsSimdSupported)
                        {
                            SimdHelper.ZeroBuffer(buffer.AsSpan(0, estimatedSize));
                        }
                        else
                        {
                            Array.Clear(buffer, 0, estimatedSize);
                        }

                        int bytesWritten = 0;
                        Span<byte> bufferSpan = buffer.AsSpan();
                        
                        foreach (var col in this.Columns)
                        {
                            int written = WriteTypedValueToSpan(bufferSpan.Slice(bytesWritten), row[col], this.ColumnTypes[this.Columns.IndexOf(col)]);
                            bytesWritten += written;
                        }

                        var rowData = buffer.AsSpan(0, bytesWritten).ToArray();
                        serializedRows.Add(rowData);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                    }
                }

                // Step 3: ✅ ROUTE TO ENGINE: Single InsertBatch() call (within transaction)!
                long[] positions = engine.InsertBatch(Name, serializedRows);

                // Step 4: Update indexes
                if (StorageMode == StorageMode.Columnar)
                {
                    // Ensure all registered indexes are loaded
                    foreach (var registeredCol in this.registeredIndexes.Keys.Where(col => !this.loadedIndexes.Contains(col)).ToList())
                    {
                        EnsureIndexLoaded(registeredCol);
                    }
                }

                // Update primary key index and hash indexes
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var position = positions[i];

                    if (this.PrimaryKeyIndex >= 0)
                    {
                        var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                        this.Index.Insert(pkVal, position);
                    }

                    // Hash indexes (only for columnar)
                    if (StorageMode == StorageMode.Columnar)
                    {
                        foreach (var hashIndex in this.hashIndexes.Values)
                        {
                            hashIndex.Add(row, position);
                        }
                    }
                }

                if (StorageMode == StorageMode.Columnar)
                {
                    foreach (var registeredCol in this.registeredIndexes.Keys.Where(col => !this.loadedIndexes.Contains(col)).ToList())
                    {
                        this.staleIndexes.Add(registeredCol);
                    }
                }

                // ✅ CRITICAL FIX: Commit transaction to flush all pages at once!
                if (needsTransaction)
                {
                    engine.CommitAsync().GetAwaiter().GetResult();
                }

                return positions;
            }
            catch
            {
                // Rollback on error
                if (needsTransaction)
                {
                    engine.Rollback();
                }
                throw;
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Selects rows from the table with optional WHERE and ORDER BY clauses.
    /// </summary>
    /// <param name="where">Optional WHERE clause.</param>
    /// <param name="orderBy">Optional ORDER BY column.</param>
    /// <param name="asc">Whether to order ascending (default true).</param>
    /// <returns>List of matching rows.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
    {
        return Select(where, orderBy, asc, false);
    }

    /// <summary>
    /// Selects rows from the table with optional WHERE, ORDER BY, and encryption bypass.
    /// </summary>
    /// <param name="where">Optional WHERE clause.</param>
    /// <param name="orderBy">Optional ORDER BY column.</param>
    /// <param name="asc">Whether to order ascending.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>List of matching rows.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        ArgumentNullException.ThrowIfNull(this.storage);
        return this.isReadOnly ? SelectInternal(where, orderBy, asc, noEncrypt) : SelectWithLock(where, orderBy, asc, noEncrypt);
    }

    private List<Dictionary<string, object>> SelectWithLock(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        this.rwLock.EnterUpgradeableReadLock();
        try
        {
            return SelectInternal(where, orderBy, asc, noEncrypt);
        }
        finally
        {
            this.rwLock.ExitUpgradeableReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Dictionary<string, object>> SelectInternal(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        var results = new List<Dictionary<string, object>>();
        var engine = GetOrCreateStorageEngine();

        // 1. HashIndex lookup (O(1)) - only for columnar storage
        if (StorageMode == StorageMode.Columnar && 
            !string.IsNullOrEmpty(where) && 
            TryParseSimpleWhereClause(where, out var col, out var valObj) && 
            this.registeredIndexes.ContainsKey(col))
        {
            EnsureIndexLoaded(col);
            
            if (this.hashIndexes.TryGetValue(col, out var hashIndex))
            {
                var colIdx = this.Columns.IndexOf(col);
                if (colIdx >= 0)
                {
                    var key = ParseValueForHashLookup(valObj.ToString() ?? string.Empty, this.ColumnTypes[colIdx]);
                    if (key is not null)
                    {
                        var positions = hashIndex.LookupPositions(key);
                        foreach (var pos in positions)
                        {
                            var data = engine.Read(Name, pos);
                            if (data != null)
                            {
                                var row = DeserializeRow(data);
                                if (row != null) results.Add(row);
                            }
                        }
                    }
                    if (results.Count > 0) return ApplyOrdering(results, orderBy, asc);
                }
            }
        }

        // 2. Primary key lookup (works for both storage modes)
        if (results.Count == 0 && where != null && this.PrimaryKeyIndex >= 0)
        {
            var pkCol = this.Columns[this.PrimaryKeyIndex];
            if (TryParseSimpleWhereClause(where, out var whereCol, out var whereVal) && whereCol == pkCol)
            {
                var pkVal = whereVal.ToString() ?? string.Empty;
                var searchResult = this.Index.Search(pkVal);
                if (searchResult.Found)
                {
                    long position = searchResult.Value;
                    var data = engine.Read(Name, position);
                    if (data != null)
                    {
                        var row = DeserializeRow(data);
                        if (row != null) results.Add(row);
                    }
                    return ApplyOrdering(results, orderBy, asc);
                }
            }
        }

        // 3. Full scan - storage mode specific
        if (results.Count == 0)
        {
            if (StorageMode == StorageMode.Columnar)
            {
                // Columnar: Read entire file and scan, filtering out deleted/stale rows
                var data = this.storage!.ReadBytes(this.DataFile, noEncrypt);
                if (data != null && data.Length > 0)
                {
                    results = ScanRowsWithSimdAndFilterStale(data, where);
                }
            }
            else // PageBased
            {
                // ✅ IMPLEMENTED: Full table scan using storage engine's GetAllRecords
                uint tableId = (uint)Name.GetHashCode();
                results = ScanPageBasedTable(tableId, where);
            }
        }

        return ApplyOrdering(results, orderBy, asc);
    }

    /// <summary>
    /// Scans rows with SIMD optimization and filters out stale versions for columnar storage.
    /// Columnar UPDATE creates new versions, so we need to only return rows whose PK points to their position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<Dictionary<string, object>> ScanRowsWithSimdAndFilterStale(byte[] data, string? where)
    {
        var results = new List<Dictionary<string, object>>();
        
        // Scan file with position tracking
        int filePosition = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        
        while (filePosition < dataSpan.Length)
        {
            // Read length prefix (4 bytes)
            if (filePosition + 4 > dataSpan.Length)
                break;
            
            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                dataSpan.Slice(filePosition, 4));
            
            const int MaxRecordSize = 1_000_000_000;
            if (recordLength < 0 || recordLength > MaxRecordSize)
            {
                Console.WriteLine($"⚠️  Invalid record length {recordLength} at position {filePosition}");
                break;
            }
            
            if (recordLength == 0)
            {
                filePosition += 4;
                continue;
            }
            
            if (filePosition + 4 + recordLength > dataSpan.Length)
            {
                Console.WriteLine($"⚠️  Incomplete record at position {filePosition}");
                break;
            }
            
            long currentRecordPosition = filePosition; // Track position for filtering
            
            // Skip length prefix and read record data
            int dataOffset = filePosition + 4;
            ReadOnlySpan<byte> recordData = dataSpan.Slice(dataOffset, recordLength);
            
            // Parse the record
            var row = new Dictionary<string, object>();
            bool valid = true;
            int offset = 0;
            
            for (int i = 0; i < this.Columns.Count; i++)
            {
                try
                {
                    var value = ReadTypedValueFromSpan(recordData.Slice(offset), this.ColumnTypes[i], out int bytesRead);
                    row[this.Columns[i]] = value;
                    offset += bytesRead;
                }
                catch
                {
                    valid = false;
                    break;
                }
            }
            
            // ✅ CRITICAL FIX: Only include row if it's the current version for its PK AND matches WHERE
            if (valid)
            {
                bool isCurrentVersion = true;
                
                // Check if this row is the current version by verifying PK index points to this position
                if (this.PrimaryKeyIndex >= 0)
                {
                    var pkCol = this.Columns[this.PrimaryKeyIndex];
                    if (row.TryGetValue(pkCol, out var pkValue) && pkValue != null)
                    {
                        var pkStr = pkValue.ToString() ?? string.Empty;
                        var searchResult = this.Index.Search(pkStr);
                        
                        // Row is current version only if PK index points to THIS position
                        isCurrentVersion = searchResult.Found && searchResult.Value == currentRecordPosition;
                    }
                }
                
                bool matchesWhere = string.IsNullOrEmpty(where) || EvaluateWhere(row, where);
                
                if (isCurrentVersion && matchesWhere)
                {
                    results.Add(row);
                }
            }
            
            filePosition += 4 + recordLength;
        }
        
        return results;
    }

    /// <summary>
    /// Updates rows in the table that match the WHERE condition.
    /// Routes to storage engine with different semantics per mode:
    /// - Columnar: Append new version (old becomes stale)
    /// - PageBased: In-place update via engine.Update()
    /// </summary>
    /// <param name="where">Optional WHERE clause to filter rows.</param>
    /// <param name="updates">Dictionary of column names and new values.</param>
    /// <exception cref="InvalidOperationException">Thrown when table is readonly.</exception>
    public void Update(string? where, Dictionary<string, object> updates)
    {
        if (this.isReadOnly) throw new InvalidOperationException("Cannot update in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            var engine = GetOrCreateStorageEngine();
            var rows = this.Select(where);
            
            foreach (var row in rows)
            {
                // Apply updates to the row
                foreach (var update in updates)
                {
                    row[update.Key] = update.Value;
                }
                
                // Serialize updated row
                int estimatedSize = EstimateRowSize(row);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);
                
                try
                {
                    if (SimdHelper.IsSimdSupported)
                    {
                        SimdHelper.ZeroBuffer(buffer.AsSpan(0, estimatedSize));
                    }
                    else
                    {
                        Array.Clear(buffer, 0, estimatedSize);
                    }

                    int bytesWritten = 0;
                    Span<byte> bufferSpan = buffer.AsSpan();
                    
                    foreach (var col in this.Columns)
                    {
                        int written = WriteTypedValueToSpan(bufferSpan.Slice(bytesWritten), row[col], this.ColumnTypes[this.Columns.IndexOf(col)]);
                        bytesWritten += written;
                    }

                    var rowData = buffer.AsSpan(0, bytesWritten).ToArray();

                    if (StorageMode == StorageMode.Columnar)
                    {
                        // Columnar: Append new version (old ref becomes stale)
                        // Get old position from primary key index
                        long oldPosition = -1;
                        if (this.PrimaryKeyIndex >= 0)
                        {
                            var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                            var searchResult = this.Index.Search(pkVal);
                            if (searchResult.Found)
                            {
                                oldPosition = searchResult.Value;
                            }
                        }

                        // Insert new version
                        long newPosition = engine.Insert(Name, rowData);

                        // Update indexes to point to new position
                        if (this.PrimaryKeyIndex >= 0)
                        {
                            var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                            this.Index.Delete(pkVal); // Remove old
                            this.Index.Insert(pkVal, newPosition); // Add new
                        }

                        // Update hash indexes
                        foreach (var hashIndex in this.hashIndexes.Values)
                        {
                            if (oldPosition >= 0)
                            {
                                hashIndex.Remove(row, oldPosition); // Remove old ref
                            }
                            hashIndex.Add(row, newPosition); // Add new ref
                        }
                        
                        // ✅ NEW: Track updates for compaction
                        Interlocked.Increment(ref _updatedRowCount);
                    }
                    else // PageBased
                    {
                        // Page-based: In-place update
                        // Get position from primary key index
                        if (this.PrimaryKeyIndex >= 0)
                        {
                            var pkVal = row[this.Columns[this.PrimaryKeyIndex]]?.ToString() ?? string.Empty;
                            var searchResult = this.Index.Search(pkVal);
                            if (searchResult.Found)
                            {
                                long position = searchResult.Value;
                                engine.Update(Name, position, rowData);
                                
                                // Index position stays the same (in-place update)
                                // No index updates needed unless indexed column changed
                            }
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                }
            }
            
            // ✅ NEW: Auto-compact if threshold reached
            if (StorageMode == StorageMode.Columnar)
            {
                TryAutoCompact();
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Deletes rows from the table that match the WHERE condition.
    /// Routes through storage engine with different semantics:
    /// - Columnar: Logical delete (remove from indexes, physical delete during compaction)
    /// - PageBased: Physical delete via engine.Delete() (marks slot as deleted)
    /// </summary>
    /// <param name="where">Optional WHERE clause to filter rows to delete.</param>
    /// <exception cref="InvalidOperationException">Thrown when table is readonly.</exception>
    public void Delete(string? where)
    {
        if (this.isReadOnly) throw new InvalidOperationException("Cannot delete in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            var engine = GetOrCreateStorageEngine();
            
            // Get rows to delete
            var rows = this.Select(where);
            
            // Delete via storage engine and update indexes
            foreach (var row in rows)
            {
                long storagePosition = -1;
                
                // Get position from primary key
                if (this.PrimaryKeyIndex >= 0)
                {
                    var pkCol = this.Columns[this.PrimaryKeyIndex];
                    if (row.TryGetValue(pkCol, out var pkValue) && pkValue != null)
                    {
                        var pkStr = pkValue.ToString() ?? string.Empty;
                        var searchResult = this.Index.Search(pkStr);
                        if (searchResult.Found)
                        {
                            storagePosition = searchResult.Value;
                        }
                    }
                }

                if (storagePosition >= 0)
                {
                    // Route to storage engine
                    if (StorageMode == StorageMode.PageBased)
                    {
                        // PageBased: Physical delete (marks slot as deleted)
                        engine.Delete(Name, storagePosition);
                    }
                    // Columnar: Logical delete only (no engine call needed)
                    // Physical space reclaimed during compaction
                    
                    // Remove from indexes (both modes)
                    if (this.PrimaryKeyIndex >= 0)
                    {
                        var pkCol = this.Columns[this.PrimaryKeyIndex];
                        if (row.TryGetValue(pkCol, out var pkValue) && pkValue != null)
                        {
                            var pkStr = pkValue.ToString() ?? string.Empty;
                            this.Index.Delete(pkStr);
                        }
                    }

                    // Remove from hash indexes (columnar mode only)
                    if (StorageMode == StorageMode.Columnar)
                    {
                        foreach (var kvp in this.hashIndexes.Where(idx => this.loadedIndexes.Contains(idx.Key)))
                        {
                            kvp.Value.Remove(row, storagePosition);
                        }
                        
                        // ✅ NEW: Track deletes for compaction
                        Interlocked.Increment(ref _deletedRowCount);
                    }
                }
            }
            
            // Mark unloaded indexes as stale (columnar only)
            if (StorageMode == StorageMode.Columnar)
            {
                foreach (var registeredCol in this.registeredIndexes.Keys.Where(col => !this.loadedIndexes.Contains(col)).ToList())
                {
                    this.staleIndexes.Add(registeredCol);
                }
                
                // ✅ NEW: Auto-compact if threshold reached
                TryAutoCompact();
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }
}
