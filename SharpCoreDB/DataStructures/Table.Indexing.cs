namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Index management for Table - includes lazy loading for hash indexes.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Creates a hash index on the specified column for fast WHERE clause lookups.
    /// Uses lazy loading: index is registered but not built until first query.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    /// <exception cref="InvalidOperationException">Thrown when column doesn't exist.</exception>
    public void CreateHashIndex(string columnName)
    {
        if (!this.Columns.Contains(columnName)) 
            throw new InvalidOperationException($"Column {columnName} not found");
        
        if (this.registeredIndexes.ContainsKey(columnName)) 
            return; // Already registered
        
        var colIdx = this.Columns.IndexOf(columnName);
        var metadata = new IndexMetadata(columnName, this.ColumnTypes[colIdx]);
        
        this.rwLock.EnterWriteLock();
        try
        {
            this.registeredIndexes[columnName] = metadata;
            // Index will be built lazily on first query
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Creates a hash index on the specified column and builds it immediately.
    /// Use this when you know the index will be used immediately.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    /// <param name="buildImmediately">If true, builds the index from existing data.</param>
    /// <exception cref="InvalidOperationException">Thrown when column doesn't exist.</exception>
    public void CreateHashIndex(string columnName, bool buildImmediately)
    {
        CreateHashIndex(columnName);
        
        if (buildImmediately)
        {
            EnsureIndexLoaded(columnName);
        }
    }

    /// <summary>
    /// Ensures that a hash index is loaded and built for the specified column.
    /// If index is already loaded, returns immediately (O(1)).
    /// If index needs building, scans table and builds index (O(n)).
    /// Thread-safe with double-check locking pattern.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <exception cref="InvalidOperationException">Thrown when index is not registered.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void EnsureIndexLoaded(string columnName)
    {
        // Fast path: check if already loaded (read lock)
        this.rwLock.EnterReadLock();
        try
        {
            if (this.loadedIndexes.Contains(columnName) && 
                !this.staleIndexes.Contains(columnName))
            {
                return; // Already loaded and fresh
            }
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
        
        // Slow path: need to build index (write lock)
        this.rwLock.EnterWriteLock();
        try
        {
            // Double-check after acquiring write lock (another thread might have built it)
            if (this.loadedIndexes.Contains(columnName) && 
                !this.staleIndexes.Contains(columnName))
            {
                return;
            }
            
            // Check if index is registered
            if (!this.registeredIndexes.TryGetValue(columnName, out var _))
            {
                throw new InvalidOperationException($"Index for column {columnName} is not registered");
            }
            
            // Build or rebuild the index
            var index = new HashIndex(this.Name, columnName);
            
            // Scan all rows and build index
            if (this.storage != null && File.Exists(this.DataFile))
            {
                var data = this.storage.ReadBytes(this.DataFile, false);
                if (data != null && data.Length > 0)
                {
                    using var ms = new MemoryStream(data);
                    using var reader = new BinaryReader(ms);
                    
                    while (ms.Position < ms.Length)
                    {
                        long position = ms.Position;
                        var row = new Dictionary<string, object>();
                        bool valid = true;
                        
                        for (int i = 0; i < this.Columns.Count; i++)
                        {
                            try 
                            { 
                                row[this.Columns[i]] = ReadTypedValue(reader, this.ColumnTypes[i]);
                            }
                            catch 
                            { 
                                valid = false; 
                                break; 
                            }
                        }
                        
                        if (valid && row.TryGetValue(columnName, out var value) && value != null)
                        {
                            index.Add(row, position);
                        }
                    }
                }
            }
            
            // Store the loaded index
            this.hashIndexes[columnName] = index;
            this.loadedIndexes.Add(columnName);
            this.staleIndexes.Remove(columnName);
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if a hash index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name to check.</param>
    /// <returns>True if hash index exists.</returns>
    public bool HasHashIndex(string columnName) => this.hashIndexes.ContainsKey(columnName);

    /// <summary>
    /// Gets hash index statistics for a column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>Index statistics or null if no index exists.</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName)
    {
        if (this.hashIndexes.TryGetValue(columnName, out var index))
        {
            return index.GetStatistics();
        }
        return null;
    }

    /// <summary>
    /// Gets lazy loading statistics for all hash indexes.
    /// Shows which indexes are registered, loaded, and their memory usage.
    /// </summary>
    /// <returns>Dictionary of column names to index status.</returns>
    public Dictionary<string, IndexLoadStatus> GetIndexLoadStatistics()
    {
        this.rwLock.EnterReadLock();
        try
        {
            var stats = new Dictionary<string, IndexLoadStatus>();
            
            foreach (var columnName in this.registeredIndexes.Keys)
            {
                var isLoaded = this.loadedIndexes.Contains(columnName);
                var isStale = this.staleIndexes.Contains(columnName);
                
                int uniqueKeys = 0;
                int totalRows = 0;
                double avgRowsPerKey = 0;
                
                // If loaded, get statistics
                if (isLoaded && this.hashIndexes.TryGetValue(columnName, out var hashIndex))
                {
                    var indexStats = hashIndex.GetStatistics();
                    uniqueKeys = indexStats.UniqueKeys;
                    totalRows = indexStats.TotalRows;
                    avgRowsPerKey = indexStats.AvgRowsPerKey;
                }
                
                stats[columnName] = new IndexLoadStatus(
                    ColumnName: columnName,
                    IsRegistered: true,
                    IsLoaded: isLoaded,
                    IsStale: isStale,
                    UniqueKeys: uniqueKeys,
                    TotalRows: totalRows,
                    AvgRowsPerKey: avgRowsPerKey
                );
            }
            
            return stats;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Status information for a hash index's lazy loading state.
    /// </summary>
    /// <param name="ColumnName">The column name.</param>
    /// <param name="IsRegistered">Whether the index is registered.</param>
    /// <param name="IsLoaded">Whether the index is built and loaded in memory.</param>
    /// <param name="IsStale">Whether the index needs rebuilding.</param>
    /// <param name="UniqueKeys">Number of unique keys (0 if not loaded).</param>
    /// <param name="TotalRows">Total rows indexed (0 if not loaded).</param>
    /// <param name="AvgRowsPerKey">Average rows per key (0 if not loaded).</param>
    public record IndexLoadStatus(
        string ColumnName,
        bool IsRegistered,
        bool IsLoaded,
        bool IsStale,
        int UniqueKeys,
        int TotalRows,
        double AvgRowsPerKey
    );

    /// <summary>
    /// Gets the total number of registered indexes (loaded + unloaded).
    /// </summary>
    public int TotalRegisteredIndexes => this.registeredIndexes.Count;

    /// <summary>
    /// Gets the number of currently loaded indexes.
    /// </summary>
    public int LoadedIndexesCount => this.loadedIndexes.Count;

    /// <summary>
    /// Gets the number of stale indexes that need rebuilding.
    /// </summary>
    public int StaleIndexesCount => this.staleIndexes.Count;

    /// <summary>
    /// Increments the usage counter for a column (for auto-indexing heuristics).
    /// </summary>
    /// <param name="columnName">The column name.</param>
    public void IncrementColumnUsage(string columnName)
    {
        lock (usageLock)
        {
            if (!columnUsage.TryGetValue(columnName, out var count))
                columnUsage[columnName] = 1;
            else
                columnUsage[columnName] = count + 1;
        }
    }

    /// <summary>
    /// Gets the column usage statistics for all columns.
    /// </summary>
    /// <returns>Readonly dictionary of column names to usage counts.</returns>
    public IReadOnlyDictionary<string, long> GetColumnUsage()
    {
        lock (this.usageLock)
        {
            return new ReadOnlyDictionary<string, long>(this.columnUsage);
        }
    }

    /// <summary>
    /// Tracks usage for all columns in the table (e.g., for SELECT *).
    /// </summary>
    public void TrackAllColumnsUsage()
    {
        lock (this.usageLock)
        {
            foreach (var col in this.Columns)
            {
                if (this.columnUsage.ContainsKey(col))
                    this.columnUsage[col]++;
                else
                    this.columnUsage[col] = 1;
            }
        }
    }

    /// <summary>
    /// Tracks usage for a specific column.
    /// </summary>
    /// <param name="columnName">The column name to track.</param>
    public void TrackColumnUsage(string columnName)
    {
        lock (this.usageLock)
        {
            if (this.columnUsage.ContainsKey(columnName))
                this.columnUsage[columnName]++;
            else
                this.columnUsage[columnName] = 1;
        }
    }

    private async Task ProcessIndexUpdatesAsync()
    {
        await foreach (var update in _indexQueue.Reader.ReadAllAsync())
        {
            foreach (var index in update.Indexes)
            {
                index.Add(update.Row, update.Position);
            }
        }
    }

    private sealed record IndexUpdate(Dictionary<string, object> Row, IEnumerable<HashIndex> Indexes, long Position);

    /// <summary>
    /// Metadata for a registered hash index (not yet loaded).
    /// </summary>
    private sealed record IndexMetadata(string ColumnName, DataType ColumnType);

    private sealed class IndexManager : IDisposable
    {
        public void Dispose()
        {
            // No resources to dispose - intentionally empty
            GC.SuppressFinalize(this);
        }
    }
}
