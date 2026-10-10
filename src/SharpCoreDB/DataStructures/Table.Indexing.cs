namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Index management for Table - includes lazy loading for hash indexes.
/// OPTIMIZED: Uses ConcurrentDictionary for lock-free column usage tracking (30-50% better concurrency).
/// </summary>
public partial class Table
{
    // ✅ OPTIMIZED: Use ConcurrentDictionary for lock-free operations
    private readonly ConcurrentDictionary<string, long> columnUsageConcurrent = new();

    // Resolve dynamically so performance tests and benchmarks can compare both backends in-process.
    // ✅ DatabaseConfig.EnableUnsafeEqualityIndex takes precedence; AppContext/env-var are fallbacks.
    private bool UseUnsafeEqualityIndex =>
        _config?.EnableUnsafeEqualityIndex ?? ResolveUnsafeEqualityIndexFlag();

    private static bool ResolveUnsafeEqualityIndexFlag()
    {
        if (AppContext.TryGetSwitch("SharpCoreDB.Indexing.UseUnsafeEqualityIndex", out var enabled))
        {
            return enabled;
        }

        var env = Environment.GetEnvironmentVariable("SHARPCOREDB_USE_UNSAFE_EQUALITY_INDEX");
        return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a hash index on the specified column for fast WHERE clause lookups.
    /// Uses lazy loading: index is registered but not built until first query.
    /// ✅ COLLATE Phase 4: Index automatically inherits column collation.
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
        var metadata = new IndexMetadata(columnName, this.ColumnTypes[colIdx], false);
        
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
    /// Creates a named hash index on the specified column for fast WHERE clause lookups.
    /// This overload supports SQL syntax: CREATE INDEX idx_name ON table(column).
    /// Uses lazy loading: index is registered but not built until first query.
    /// ✅ COLLATE Phase 4: Index automatically inherits column collation.
    /// </summary>
    /// <param name="indexName">The index name (e.g., "idx_email").</param>
    /// <param name="columnName">The column name to index (e.g., "email").</param>
    /// <param name="isUnique">Whether to enforce uniqueness (default: false).</param>
    /// <exception cref="InvalidOperationException">Thrown when column doesn't exist or index name already used.</exception>
    public void CreateHashIndex(string indexName, string columnName, bool isUnique = false)
    {
        if (!this.Columns.Contains(columnName)) 
            throw new InvalidOperationException($"Column {columnName} not found");
        
        this.rwLock.EnterWriteLock();
        try
        {
            // Check if index name already exists
            if (this.indexNameToColumn.ContainsKey(indexName))
                throw new InvalidOperationException($"Index {indexName} already exists");
            
            // Register the column-based index if not already registered
            if (!this.registeredIndexes.TryGetValue(columnName, out var metadata))
            {
                var colIdx = this.Columns.IndexOf(columnName);
                metadata = new IndexMetadata(columnName, this.ColumnTypes[colIdx], isUnique);
                this.registeredIndexes[columnName] = metadata;
            }
            else if (isUnique && !metadata.IsUnique)
            {
                this.registeredIndexes[columnName] = metadata with { IsUnique = true };
                this.hashIndexes.Remove(columnName);
                this.loadedIndexes.Remove(columnName);
                this.staleIndexes.Remove(columnName);
            }
            
            // Map index name to column name
            this.indexNameToColumn[indexName] = columnName;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    // ✅ OPTIMIZED: Lock-free fast-path cache for loaded, non-stale indexes.
    // Set when index is loaded under write lock; cleared when marked stale.
    // Avoids ReaderWriterLockSlim acquisition on every point-lookup query.
    private readonly ConcurrentDictionary<string, byte> _indexReadyCache = new();

    /// <summary>
    /// Ensures that a hash index is loaded and built for the specified column.
    /// If index is already loaded, returns immediately (O(1)).
    /// If index needs building, scans table and builds index (O(n)).
    /// Thread-safe with double-check locking pattern.
    /// ✅ COLLATE Phase 4: Creates index with column collation.
    /// OPTIMIZED: Lock-free fast path avoids rwLock for the common "already loaded" case.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <exception cref="InvalidOperationException">Thrown when index is not registered.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void EnsureIndexLoaded(string columnName)
    {
        // Lock-free fast path: check concurrent ready cache
        if (_indexReadyCache.ContainsKey(columnName))
        {
            return; // Already loaded and fresh — no lock needed
        }

        // Slow path: check under read lock (handles stale/not-yet-cached states)
        this.rwLock.EnterReadLock();
        try
        {
            if (this.loadedIndexes.Contains(columnName) && 
                !this.staleIndexes.Contains(columnName))
            {
                _indexReadyCache.TryAdd(columnName, 0);
                return; // Already loaded and fresh
            }
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
        
        // ✅ OPTIMIZED: Build index OUTSIDE write lock
        // Check if index is registered
        this.rwLock.EnterReadLock();
        IndexMetadata? metadata;
        try
        {
            if (!this.registeredIndexes.TryGetValue(columnName, out metadata))
            {
                throw new InvalidOperationException($"Index for column {columnName} is not registered");
            }
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
        
        // ✅ COLLATE Phase 4: Get column collation
        var colIdx = this.Columns.IndexOf(columnName);
        var collation = colIdx >= 0 && colIdx < this.ColumnCollations.Count 
            ? this.ColumnCollations[colIdx] 
            : CollationType.Binary;
        
        // Build index WITHOUT holding write lock (parallel work allowed)
        // ✅ COLLATE Phase 4: Pass collation to HashIndex constructor
        var index = new HashIndex(this.Name, columnName, collation, metadata.IsUnique, UseUnsafeEqualityIndex);
        
        if (StorageMode == SharpCoreDB.Storage.Hybrid.StorageMode.PageBased)
        {
            // PageBased: iterate all records via storage engine
            var eng = GetOrCreateStorageEngine();
            foreach (var (pos, recordData) in eng.GetAllRecords(Name))
            {
                var row = DeserializeRowFromSpan(recordData);
                if (row != null && row.ContainsKey(columnName))
                    index.Add(row, pos);
            }
        }
        else if (this.storage != null && File.Exists(this.DataFile) && this.storage.AreRecordsEncrypted(this.DataFile))
        {
            // At-rest file: records are ciphertext behind an 8-byte magic header with a per-record GCM
            // frame, so the plaintext walk below cannot parse them (it reads the header as a record
            // length and stops, leaving an EMPTY index — which made every indexed lookup miss).
            // ReadAllRecords yields each record DECRYPTED together with the PHYSICAL offset of its
            // 4-byte length prefix: exactly the position the index stores and the read path resolves,
            // and it skips tombstones for us.
            foreach (var (recordOffset, recordData) in this.storage.ReadAllRecords(this.DataFile))
            {
                var record = _fixedWidthRecords
                    ? DeserializeRowFixedWidth(recordData.AsSpan())
                    : DeserializeRowFromSpan(recordData);

                // Only the CURRENT version of a row is indexed: a length-changing update leaves its
                // superseded record in the file, and the scan that replaces this index filters those by
                // the PK pointer. Without the same filter here the index answered with rows the scan
                // never returns (see IsCurrentRecordVersion).
                if (record is not null
                    && record.TryGetValue(columnName, out var recordValue)
                    && recordValue != null
                    && IsCurrentRecordVersion(record, recordOffset))
                {
                    index.Add(record, recordOffset);
                }
            }
        }
        else if (this.storage != null && File.Exists(this.DataFile))
        {
            // PERF: Use a single buffered FileStream with SequentialScan hint instead of
            // per-record RandomAccess.Read calls via storage.ReadBytesFrom(). For 100k rows
            // this reduces syscalls from ~200k to ~150-200 buffered reads.
            using var fs = new FileStream(
                this.DataFile, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, bufferSize: 65536, FileOptions.SequentialScan);

            Span<byte> lengthBuf = stackalloc byte[4];
            long position = 0;

            while (fs.Position < fs.Length)
            {
                if (fs.Read(lengthBuf) < 4)
                    break;

                int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBuf);

                // Tombstoned record: the prefix stores the negative slot size to skip. Treating this as
                // end-of-data (the old `length <= 0 → break`) hid every live row behind the first
                // deleted one from the index.
                if (length < 0)
                {
                    int slotSize = -length;
                    if (slotSize < 4)
                        break;

                    fs.Seek(slotSize - 4, SeekOrigin.Current); // the 4 prefix bytes are already read
                    position += slotSize;
                    continue;
                }

                // Empty slot (no payload): nothing to index.
                if (length == 0)
                {
                    position += 4;
                    continue;
                }

                if (length > 1_000_000_000)
                    break;

                byte[] rowData = new byte[length];
                if (fs.Read(rowData) < length)
                    break;

                // Decode with the layout the records were WRITTEN with: the fixed-width codec (constant
                // slots + overflow arena) or the variable-length record format. Parsing fixed-width
                // records with the variable-length walk silently produced a near-empty index — and
                // because CREATE TABLE registers a hash index for every column, that made a
                // `WHERE <non-unique column> = value` lookup return only the rows that happened to
                // parse instead of every match.
                var row = _fixedWidthRecords
                    ? DeserializeRowFixedWidth(rowData.AsSpan())
                    : DeserializeRowFromSpan(rowData);

                // Same current-version filter as the at-rest walk above: this walk sees the superseded
                // records of a length-changing update, which the scan (PK-pointer rule) never returns.
                if (row is not null
                    && row.TryGetValue(columnName, out var indexedValue)
                    && indexedValue != null
                    && IsCurrentRecordVersion(row, position))
                {
                    index.Add(row, position);
                }

                position += 4 + length;
            }
        }
        
        // ✅ OPTIMIZED: Quick swap under write lock (1-2ms instead of 100ms+)
        this.rwLock.EnterWriteLock();
        try
        {
            // Double-check after acquiring write lock (another thread might have built it)
            if (this.loadedIndexes.Contains(columnName) && 
                !this.staleIndexes.Contains(columnName))
            {
                return; // Another thread built it
            }
            
            // Store the loaded index
            this.hashIndexes[columnName] = index;
            this.loadedIndexes.Add(columnName);
            this.staleIndexes.Remove(columnName);
            _indexReadyCache.TryAdd(columnName, 0);
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Loads every registered hash index up front so DML write paths (the append-only UPDATE
    /// fallback and the DELETE index cleanup) can maintain them incrementally. An unloaded index
    /// is rebuilt from the data file on next use — which, after an append update or logical
    /// delete, still contains the stale record — so any write that creates stale versions must
    /// ensure its registered indexes are loaded first. Cheap after the first load (cached).
    /// <para>
    /// <b>PageBased is deliberately exempt.</b> The reason this pre-load exists is a rebuild reading a
    /// <i>version-bearing</i> file: on the append/Columnar engines a superseded record stays in the data file
    /// and is still enumerated, which is the "stale row returned for the same PK" regression that
    /// <c>54b0a5b8</c> fixed by loading every index before the write. PageBased has no such version to leak —
    /// <c>PageManager.UpdateRecord</c> either rewrites the slot in place or moves the slot pointer inside
    /// the page (the old bytes are never enumerated), and when the page is full it marks the old slot
    /// <c>RecordFlags.Deleted</c> and inserts the record elsewhere; <c>PageManager.GetAllRecordsInPage</c>
    /// yields only slots that are not flagged deleted, and <c>PageManager.TryReadRecord</c> returns false
    /// for them. A PageBased rebuild therefore reads exactly the live rows, so deferring it is not a correctness
    /// risk — while paying it up front costs one full-table decode pass per batch UPDATE entry: measured
    /// (<c>--pk-profile --engine=pagebased</c>, fix-state independent) <c>row-decode</c> 100,000 calls for a
    /// 10,000-row UPDATE batch over 100,000 rows, 60.2 MB of decode garbage, and 42.60 → 9.73 µs/update with the
    /// pre-load skipped.
    /// </para>
    /// </summary>
    private void EnsureAllRegisteredIndexesLoaded()
    {
        if (this.registeredIndexes.Count == 0)
            return;

        if (StorageMode == SharpCoreDB.Storage.Hybrid.StorageMode.PageBased)
            return;

        // Safe to iterate directly: the caller holds the write lock and EnsureIndexLoaded only
        // mutates hashIndexes/loadedIndexes/staleIndexes, never the registeredIndexes registry.
        foreach (var columnName in this.registeredIndexes.Keys)
        {
            EnsureIndexLoaded(columnName);
        }
    }

    /// <summary>
    /// Whether automatic per-column hash indexes may be created for this table at all — the same
    /// condition the Columnar <c>CREATE TABLE</c> path applies (<c>SqlParser.DDL.cs</c>): Columnar storage
    /// and <c>DatabaseConfig.EnableHashIndexes</c> (default <c>true</c>). PageBased never auto-creates hash
    /// indexes — its row lookups are served by B-trees and its records are never versioned.
    /// </summary>
    internal bool AutoHashIndexesEnabled =>
        this.StorageMode == SharpCoreDB.Storage.Hybrid.StorageMode.Columnar &&
        (this._config?.EnableHashIndexes ?? true);

    /// <summary>
    /// Registers the automatic hash index for <paramref name="columnName"/> the first time an operation
    /// actually filters on that column, and reports whether a hash index can serve such a lookup.
    /// <para>
    /// This is §9 row 5's lever, owner-decided 2026-09-26: <em>registration</em> used to be eager (one index
    /// per column at <c>CREATE TABLE</c>), and because <see cref="EnsureAllRegisteredIndexesLoaded"/> then
    /// loads every registered index before a write, a workload that filters on one column paid index
    /// maintenance for all of them — 20.000 <c>index-maint</c> calls per 10.000 updates on the five-column
    /// <c>docs</c> shape. Registering on demand keeps exactly the indexes the workload uses.
    /// </para>
    /// <para>
    /// Every genuine lookup gate in the read/write paths calls this instead of testing
    /// <c>registeredIndexes.ContainsKey</c>, so "may this column use a hash index" has ONE answer (and the
    /// on-demand registration happens at the same moment, in the same place, for all of them). Returning
    /// <c>true</c> for an already-registered column (explicit <c>CREATE INDEX</c> included) means the
    /// callers' existing <c>EnsureIndexLoaded</c> + lookup flow is unchanged.
    /// </para>
    /// </summary>
    /// <param name="columnName">The column a filtered operation targets.</param>
    /// <returns>True when a hash index serves this column — already registered, or just registered.</returns>
    public bool EnsureAutoHashIndexRegistered(string columnName)
    {
        if (string.IsNullOrEmpty(columnName) || !this.Columns.Contains(columnName))
        {
            return false;
        }

        // Hot path: already registered (auto, or explicitly via CREATE INDEX) — no lock, mirroring the
        // lock-free reads the lookup gates already perform on this registry.
        if (this.registeredIndexes.ContainsKey(columnName))
        {
            return true;
        }

        if (!this.AutoHashIndexesEnabled)
        {
            return false;
        }

        var colIdx = this.Columns.IndexOf(columnName);
        var metadata = new IndexMetadata(columnName, this.ColumnTypes[colIdx], false);

        // Registration mutates the registry, so it belongs under the write lock — EXCEPT when the caller
        // already holds it: the write paths (UPDATE/DELETE) resolve their rows through SelectInternal,
        // which can reach here while the write lock is held, and ReaderWriterLockSlim's default
        // non-recursive policy throws on a second EnterWriteLock (the same reason
        // EnsureAllRegisteredIndexesLoaded documents the pre-load it performs).
        bool ownsLock = !this.rwLock.IsWriteLockHeld;
        if (ownsLock)
        {
            this.rwLock.EnterWriteLock();
        }

        try
        {
            if (this.registeredIndexes.ContainsKey(columnName))
            {
                return true; // another thread registered it while we waited
            }

            this.registeredIndexes[columnName] = metadata;
            return true;
        }
        finally
        {
            if (ownsLock)
            {
                this.rwLock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// True when the record at <paramref name="recordPosition"/> is the <b>current</b> version of its row —
    /// the exact rule the Columnar scan uses (<c>ScanRowsWithSimdAndFilterStale</c>): with a primary key
    /// (including the hidden <c>_rowid</c> fallback every PK-less table gets), the current version is the
    /// one the PK index points at.
    /// <para>
    /// Why it is one shared method: an index built by walking the data file and the scan that replaces it
    /// must never disagree about which rows exist. The Columnar engines append a new version on a
    /// length-changing update and leave the superseded record in the file (compaction drops it), so a
    /// rebuild that ignores this rule indexes rows the scan does not return — measured session 71: a hash
    /// index created <em>after</em> such an update answered <c>WHERE v = 'old'</c> with the superseded row
    /// (1 row where SQLite returns 0).
    /// </para>
    /// </summary>
    /// <param name="row">The deserialized record.</param>
    /// <param name="recordPosition">The record's physical length-prefix offset (the value the index stores).</param>
    /// <returns>True when the record is the row's current version (or when no witness exists).</returns>
    internal bool IsCurrentRecordVersion(Dictionary<string, object> row, long recordPosition)
    {
        if (this.PrimaryKeyIndex < 0)
        {
            return true;
        }

        var pkColumn = this.Columns[this.PrimaryKeyIndex];
        if (!row.TryGetValue(pkColumn, out var pkValue) || pkValue is null)
        {
            return true; // no PK value to compare with — the scan includes such rows too
        }

        var searchResult = this.Index.Search(pkValue.ToString() ?? string.Empty);
        if (!searchResult.Found)
        {
            return false; // PK removed from the index → the row is deleted
        }

        if (searchResult.Value == 0)
        {
            // Position was not tracked during a batch insert — the scan includes these, so the index must too.
            return true;
        }

        return searchResult.Value == recordPosition;
    }

    /// <summary>
    /// Checks if a hash index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name to check.</param>
    /// <returns>True if hash index exists.</returns>
    public bool HasHashIndex(string columnName) => this.hashIndexes.ContainsKey(columnName);
    
    /// <summary>
    /// Snapshot of the table's registered indexes for catalog introspection
    /// (<c>information_schema.indexes</c>). One entry per index name; the automatic per-column hash
    /// indexes created without a name are reported under their column name.
    /// </summary>
    /// <returns>Read-only snapshot: index name, indexed column, index kind ("HASH"/"BTREE"), uniqueness.</returns>
    public IReadOnlyList<(string IndexName, string ColumnName, string IndexType, bool IsUnique)> GetIndexCatalogSnapshot()
    {
        this.rwLock.EnterReadLock();
        try
        {
            var snapshot = new List<(string, string, string, bool)>(this.registeredIndexes.Count + this.indexNameToColumn.Count);
            var namedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (indexName, columnName) in this.indexNameToColumn)
            {
                namedColumns.Add(columnName);
                var isUnique = this.registeredIndexes.TryGetValue(columnName, out var named) && named.IsUnique;
                snapshot.Add((indexName, columnName, ResolveRegisteredIndexKind(columnName), isUnique));
            }

            foreach (var (columnName, metadata) in this.registeredIndexes)
            {
                if (namedColumns.Contains(columnName))
                    continue;

                snapshot.Add((columnName, columnName, ResolveRegisteredIndexKind(columnName), metadata.IsUnique));
            }

            return snapshot;
        }
        finally
        {
            this.rwLock.ExitReadLock();
        }
    }

    /// <summary>Reports which index kind is maintained for a column (a B-tree wins when both exist).</summary>
    private string ResolveRegisteredIndexKind(string columnName)
        => this._btreeManager?.HasIndex(columnName) == true ? "BTREE" : "HASH";
    
    /// <summary>
    /// Checks if an index with the specified name exists (by name or column).
    /// ✅ Phase 1.5: Added for IF NOT EXISTS support in CREATE INDEX.
    /// </summary>
    /// <param name="nameOrColumn">Index name (e.g., "idx_email") or column name (e.g., "email").</param>
    /// <returns>True if index exists.</returns>
    public bool HasIndex(string nameOrColumn)
    {
        // Check index name first
        if (this.indexNameToColumn.ContainsKey(nameOrColumn))
            return true;
        
        // Check column-based hash index
        if (this.hashIndexes.ContainsKey(nameOrColumn))
            return true;
        
        // Check B-tree indexes via manager
        if (this._btreeManager != null && this._btreeManager.HasIndex(nameOrColumn))
            return true;
        
        return false;
    }

    /// <summary>
    /// Removes a hash index for the specified column or index name.
    /// Also removes B-tree indexes if they exist for the same column.
    /// Supports both column-based removal and named index removal.
    /// </summary>
    /// <param name="columnName">The index name (e.g., "idx_email") or column name (e.g., "email").</param>
    /// <returns>True if index was removed, false if it didn't exist.</returns>
    public bool RemoveHashIndex(string columnName)
    {
        this.rwLock.EnterWriteLock();
        try
        {
            bool removed = false;
            string? targetColumn = null;
            
            // Check if this is an index name
            if (this.indexNameToColumn.TryGetValue(columnName, out var mappedColumn))
            {
                // It's an index name - remove the mapping and use the column name
                targetColumn = mappedColumn;
                this.indexNameToColumn.Remove(columnName);
                removed = true;
            }
            else
            {
                // It's a column name directly
                targetColumn = columnName;
            }
            
            // Remove the actual hash index structures for the column
            if (this.hashIndexes.Remove(targetColumn))
                removed = true;
            
            if (this.registeredIndexes.Remove(targetColumn))
                removed = true;
            
            if (this.loadedIndexes.Remove(targetColumn))
                removed = true;
            
            this.staleIndexes.Remove(targetColumn);
            
            // ✅ NEW: Also remove B-tree index if it exists
            if (RemoveBTreeIndexInternal(targetColumn))
                removed = true;
            
            return removed;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// PERFORMANCE CRITICAL: Clears ALL indexes (hash indexes, B-tree indexes, registrations, and state).
    /// Used when table is dropped or recreated to ensure complete cleanup.
    /// This prevents stale/corrupt index data from being read after DDL operations.
    /// 
    /// USAGE:
    /// - Called by DROP TABLE to clean up before deletion
    /// - Called by CREATE TABLE to ensure fresh start (if table name is reused)
    /// - NOT called by normal DML operations (INSERT/UPDATE/DELETE use stale marking)
    /// </summary>
    public void ClearAllIndexes()
    {
        this.rwLock.EnterWriteLock();
        try
        {
            // Clear all index data structures
            this.hashIndexes.Clear();
            this.registeredIndexes.Clear();
            this.loadedIndexes.Clear();
            this.staleIndexes.Clear();
            this.indexNameToColumn.Clear(); // 🔥 CRITICAL: Also clear name→column mapping
            
            // ✅ NEW: Clear B-tree indexes too
            ClearBTreeIndexes();
            
            // Also clear column usage statistics (fresh table = fresh stats)
            this.columnUsageConcurrent.Clear();
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

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
    /// OPTIMIZED: Lock-free using ConcurrentDictionary.AddOrUpdate.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    public void IncrementColumnUsage(string columnName)
    {
        columnUsageConcurrent.AddOrUpdate(columnName, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Gets the column usage statistics for all columns.
    /// OPTIMIZED: Returns snapshot from ConcurrentDictionary (lock-free read).
    /// </summary>
    /// <returns>Readonly dictionary of column names to usage counts.</returns>
    public IReadOnlyDictionary<string, long> GetColumnUsage()
    {
        // Return snapshot of current state (thread-safe)
        return new ReadOnlyDictionary<string, long>(
            new Dictionary<string, long>(columnUsageConcurrent));
    }

    /// <summary>
    /// Tracks usage for all columns in the table (e.g., for SELECT *).
    /// OPTIMIZED: Lock-free using ConcurrentDictionary.
    /// </summary>
    public void TrackAllColumnsUsage()
    {
        foreach (var col in this.Columns)
        {
            columnUsageConcurrent.AddOrUpdate(col, 1, (_, count) => count + 1);
        }
    }

    /// <summary>
    /// Tracks usage for a specific column.
    /// OPTIMIZED: Lock-free using ConcurrentDictionary.
    /// </summary>
    /// <param name="columnName">The column name to track.</param>
    public void TrackColumnUsage(string columnName)
    {
        columnUsageConcurrent.AddOrUpdate(columnName, 1, (_, count) => count + 1);
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
    private sealed record IndexMetadata(string ColumnName, DataType ColumnType, bool IsUnique = false);

    private sealed class IndexManager : IDisposable
    {
        public void Dispose()
        {
            // No resources to dispose - intentionally empty
            GC.SuppressFinalize(this);
        }
    }
}
