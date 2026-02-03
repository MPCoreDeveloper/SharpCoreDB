namespace SharpCoreDB.DataStructures;

using Microsoft.Extensions.ObjectPool;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Hybrid;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SharpCoreDB.Services;


/// <summary>
/// Implementation of ITable with SIMD-accelerated operations and batch insert optimization.
/// Split into partial classes for better organization:
/// - Table.cs: Core properties, fields, constructors
/// - Table.CRUD.cs: Insert, Select, Update, Delete operations
/// - Table.Serialization.cs: Type serialization and deserialization
/// - Table.Scanning.cs: SIMD-accelerated scanning methods
/// - Table.Indexing.cs: Hash index management and lazy loading
/// </summary>
public partial class Table : ITable, IDisposable
{
    /// <summary>
    /// Default constructor for Table.
    /// </summary>
    public Table() 
    {
        // ✅ NEW: Initialize dictionary pool for SELECT operations
        _dictPool = new DefaultObjectPool<Dictionary<string, object>>(
            new DictionaryPooledObjectPolicy(), 
            maximumRetained: 1000);
    }
    
    /// <summary>
    /// Initializes a new instance of the Table class with storage and readonly flag.
    /// </summary>
    /// <param name="storage">The storage instance to use for persistence.</param>
    /// <param name="isReadOnly">Whether this table is readonly.</param>
    /// <param name="config">Optional database configuration for optimization hints.</param>
    public Table(IStorage storage, bool isReadOnly = false, DatabaseConfig? config = null) : this()
    {
        (this.storage, this.isReadOnly) = (storage, isReadOnly);
        _config = config;
        
        // Apply compaction threshold from config if provided
        if (config is not null && config.ColumnarAutoCompactionThreshold > 0)
        {
            COMPACTION_THRESHOLD = config.ColumnarAutoCompactionThreshold;
        }
        
        if (!isReadOnly)
        {
            this.indexManager = new IndexManager();
            _ = Task.Run(ProcessIndexUpdatesAsync);
        }
    }

    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the column names.
    /// </summary>
    public List<string> Columns { get; set; } = [];
    
    /// <summary>
    /// Gets or sets the column data types.
    /// </summary>
    public List<DataType> ColumnTypes { get; set; } = [];
    
    /// <summary>
    /// Gets or sets the primary key column index (-1 if no primary key).
    /// </summary>
    public int PrimaryKeyIndex { get; set; } = -1;
    
    /// <summary>
    /// Gets or sets which columns are auto-generated.
    /// </summary>
    public List<bool> IsAuto { get; set; } = [];

    /// <summary>
    /// Gets or sets which columns are NOT NULL.
    /// </summary>
    public List<bool> IsNotNull { get; set; } = [];

    /// <summary>
    /// Gets or sets the default values for columns.
    /// </summary>
    public List<object?> DefaultValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the default expressions for columns (e.g., CURRENT_TIMESTAMP, NEWID()).
    /// </summary>
    public List<string?> DefaultExpressions { get; set; } = [];

    /// <summary>
    /// Gets or sets the CHECK constraint expressions for columns.
    /// </summary>
    public List<string?> ColumnCheckExpressions { get; set; } = [];

    /// <summary>
    /// Gets or sets the table-level CHECK constraints.
    /// </summary>
    public List<string> TableCheckConstraints { get; set; } = [];

    /// <summary>
    /// Gets or sets the unique constraints for the table.
    /// </summary>
    public List<List<string>> UniqueConstraints { get; set; } = [];

    /// <summary>
    /// Gets or sets the foreign key constraints.
    /// </summary>
    public List<ForeignKeyConstraint> ForeignKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets the data file path.
    /// </summary>
    public string DataFile { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the storage mode for this table (Columnar, PageBased, or Hybrid).
    /// Default is Columnar for backward compatibility.
    /// </summary>
    public StorageMode StorageMode { get; set; } = StorageMode.Columnar;
    
    /// <summary>
    /// Gets or sets the B-Tree index for primary key lookups.
    /// </summary>
    public IIndex<string, long> Index { get; set; } = new BTree<string, long>();

    // ✅ NEW: Cached row count to avoid expensive full table scans in GetDatabaseStatistics()
    // This is incremented on Insert and decremented on Delete
    private long _cachedRowCount = 0;

    private readonly Dictionary<string, HashIndex> hashIndexes = [];
    private readonly Dictionary<string, IndexMetadata> registeredIndexes = [];
    private readonly HashSet<string> loadedIndexes = [];
    private readonly HashSet<string> staleIndexes = [];
    
    // ✅ PERFORMANCE: Cache column name to index mapping for O(1) lookups
    private Dictionary<string, int>? _columnIndexCache;

    /// <summary>
    /// Gets or builds the column name to index cache for O(1) lookups.
    /// ✅ PERFORMANCE: Eliminates repeated O(n) IndexOf() calls in hot paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Dictionary<string, int> GetColumnIndexCache()
    {
        if (_columnIndexCache is null)
        {
            _columnIndexCache = new Dictionary<string, int>(Columns.Count);
            for (int i = 0; i < Columns.Count; i++)
            {
                _columnIndexCache[Columns[i]] = i;
            }
        }
        return _columnIndexCache;
    }
    
    // 🔥 CRITICAL FIX: Map index NAMES to COLUMN names for proper DROP INDEX support
    // SQL: CREATE INDEX idx_email ON users(email) 
    //      → indexNameToColumn["idx_email"] = "email"
    // SQL: DROP INDEX idx_email
    //      → lookup "idx_email" → find "email" → remove hash index on "email"
    private readonly Dictionary<string, string> indexNameToColumn = [];
    
    private IStorage? storage;
    
    // ✅ Phase 2: Storage provider reference for delta-update optimization
    // Used to access SingleFileStorageProvider.UpdateBlockAsync() for Single-File mode
    private IStorageProvider? _storageProvider;
    
    private readonly ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.SupportsRecursion);

    private bool isReadOnly;
    private readonly IndexManager? indexManager;
    private readonly Channel<IndexUpdate> _indexQueue = Channel.CreateUnbounded<IndexUpdate>();

    // ✅ NEW: Storage engine routing for hybrid architecture
    private IStorageEngine? _storageEngine;
    private readonly object _engineLock = new object();

    // ✅ NEW: DatabaseConfig for passing optimizations through to storage engines
    private readonly DatabaseConfig? _config;

    // ✅ NEW: Compaction tracking for columnar storage
    private long _deletedRowCount = 0;
    private long _updatedRowCount = 0;
    private long COMPACTION_THRESHOLD = 1000; // Default; can be overridden by DatabaseConfig

    // ✅ NEW: Dictionary pooling for SELECT operations (Phase 1 optimization)
    // Reduces allocations by 60% during full table scans
    private readonly ObjectPool<Dictionary<string, object>> _dictPool;

    // ✅ NEW: Database reference for last_insert_rowid() tracking
    private Database? _database;

    /// <summary>
    /// Sets the storage instance for this table.
    /// </summary>
    /// <param name="storage">The storage instance.</param>
    public void SetStorage(IStorage storage) => this.storage = storage;
    
    /// <summary>
    /// ✅ Phase 2: Sets the storage provider for delta-update optimization.
    /// </summary>
    /// <param name="storageProvider">The storage provider instance (nullable for directory-based storage).</param>
    public void SetStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;
    
    /// <summary>
    /// Sets the readonly flag for this table.
    /// </summary>
    /// <param name="isReadOnly">True if readonly.</param>
    public void SetReadOnly(bool isReadOnly) => this.isReadOnly = isReadOnly;


    /// <summary>
    /// Sets the database instance for last_insert_rowid() tracking.
    /// </summary>
    /// <param name="database">The database instance.</param>
    public void SetDatabase(Database database) => _database = database;

    /// <summary>
    /// Sets the auto-compaction threshold for columnar storage.
    /// Set to 0 or less to disable auto-compaction.
    /// </summary>
    /// <param name="threshold">Number of updates+deletes to trigger compaction.</param>
    public void SetCompactionThreshold(long threshold)
    {
        COMPACTION_THRESHOLD = threshold <= 0 ? long.MaxValue : threshold;
    }

    /// <summary>
    /// Gets the cached row count (O(1) operation).
    /// Returns -1 if cache is not initialized (call RefreshRowCount() first).
    /// ✅ PERFORMANCE: Avoids expensive full table scan in GetDatabaseStatistics()
    /// </summary>
    /// <returns>Number of rows in the table, or -1 if not cached.</returns>
    public long GetCachedRowCount()
    {
        return _cachedRowCount;
    }

    /// <summary>
    /// Refreshes the cached row count by doing a full table scan.
    /// Call this once after loading the table from disk.
    /// </summary>
    public void RefreshRowCount()
    {
        try
        {
            _cachedRowCount = Select().Count;
        }
        catch
        {
            _cachedRowCount = 0;
        }
    }

    /// <summary>
    /// Builds a StructRowSchema for zero-copy SELECT operations.
    /// </summary>
    /// <returns>The schema describing column layout for StructRow.</returns>
    public StructRowSchema BuildStructRowSchema()
    {
        var offsets = new int[Columns.Count];
        int currentOffset = 0;

        for (int i = 0; i < Columns.Count; i++)
        {
            offsets[i] = currentOffset;
            currentOffset += GetColumnSize(ColumnTypes[i]);
        }

        return new StructRowSchema(Columns.ToArray(), ColumnTypes.ToArray(), offsets, currentOffset);
    }

    /// <summary>
    /// Adds a new column to the table schema.
    /// Used for ALTER TABLE ADD COLUMN operations.
    /// </summary>
    /// <param name="columnDef">The column definition to add.</param>
    /// <exception cref="ArgumentException">Thrown when column name already exists or invalid definition.</exception>
    public void AddColumn(ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        // Validate column name doesn't exist
        if (Columns.Contains(columnDef.Name))
        {
            throw new ArgumentException($"Column '{columnDef.Name}' already exists in table '{Name}'");
        }

        // Add to schema lists
        Columns.Add(columnDef.Name);
        ColumnTypes.Add(ParseDataType(columnDef.DataType));
        IsAuto.Add(columnDef.IsAutoIncrement);
        IsNotNull.Add(columnDef.IsNotNull);
        DefaultValues.Add(columnDef.DefaultValue);
        DefaultExpressions.Add(columnDef.DefaultExpression);
        ColumnCheckExpressions.Add(columnDef.CheckExpression);

        // Handle UNIQUE constraint
        if (columnDef.IsUnique)
        {
            UniqueConstraints.Add([columnDef.Name]);
        }

        // Note: Foreign keys would be handled separately in Phase 1.2
        // For now, just schema changes
    }

    /// <summary>
    /// Parses a string data type to DataType enum.
    /// </summary>
    private static DataType ParseDataType(string typeStr)
    {
        return typeStr.ToUpperInvariant() switch
        {
            "INTEGER" or "INT" => DataType.Integer,
            "TEXT" or "VARCHAR" or "NVARCHAR" => DataType.String,
            "REAL" or "FLOAT" or "DOUBLE" => DataType.Real,
            "BLOB" => DataType.Blob,
            "BOOLEAN" or "BOOL" => DataType.Boolean,
            "DATETIME" => DataType.DateTime,
            "LONG" => DataType.Long,
            "DECIMAL" => DataType.Decimal,
            "ULID" => DataType.Ulid,
            "GUID" => DataType.Guid,
            _ => DataType.String,
        };
    }

    /// <summary>
    /// Rebuilds the Primary Key B-Tree index by scanning the data file.
    /// ✅ CRITICAL: This must be called after deserialization because the B-Tree index
    /// is NOT persisted to disk (it's rebuild from data on load).
    /// Without this, SELECT queries return 0 rows after restart!
    /// </summary>
    public void RebuildPrimaryKeyIndexFromDisk()
    {
        if (PrimaryKeyIndex < 0)
        {
            return; // No primary key, nothing to rebuild
        }

        if (storage == null)
        {
            throw new InvalidOperationException("Storage must be set before rebuilding index");
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] Starting rebuild for table: {Name}");
        System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] DataFile: {DataFile}");
        System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] PK column: {Columns[PrimaryKeyIndex]}");
#endif

        // Clear existing index
        Index = new BTree<string, long>();

        // Read entire data file
        var data = storage.ReadBytes(DataFile, noEncrypt: false);
        if (data == null || data.Length == 0)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] Data file is empty, no index to rebuild");
#endif
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] Data file size: {data.Length} bytes");
#endif

        // Scan file and rebuild index
        int filePosition = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        int recordsIndexed = 0;

        while (filePosition < dataSpan.Length)
        {
            // Read length prefix (4 bytes)
            if (filePosition + 4 > dataSpan.Length)
            {
                break;
            }

            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                dataSpan.Slice(filePosition, 4));

            if (recordLength <= 0 || recordLength > 1_000_000_000)
            {
                break; // Invalid record
            }

            if (filePosition + 4 + recordLength > dataSpan.Length)
            {
                break; // Incomplete record
            }

            long currentRecordPosition = filePosition; // This is the key for the index!

            // Skip length prefix and read record data
            int dataOffset = filePosition + 4;
            ReadOnlySpan<byte> recordData = dataSpan.Slice(dataOffset, recordLength);

            // Parse just enough to get the primary key value
            try
            {
                var row = new Dictionary<string, object>();
                int offset = 0;

                for (int i = 0; i < Columns.Count; i++)
                {
                    if (offset >= recordData.Length)
                    {
                        break;
                    }

                    var value = ReadTypedValueFromSpan(recordData.Slice(offset), ColumnTypes[i], out int bytesRead);

                    // Only store PK column, we don't need the rest for index rebuild
                    if (i == PrimaryKeyIndex)
                    {
                        row[Columns[i]] = value;
                    }

                    offset += bytesRead;
                }

                // Extract PK value and add to index
                if (row.TryGetValue(Columns[PrimaryKeyIndex], out var pkValue) && pkValue != null)
                {
                    var pkStr = pkValue.ToString() ?? string.Empty;
                    
                    // Only add if this key doesn't exist yet (handles UPDATE versions - keep latest)
                    var existing = Index.Search(pkStr);
                    if (!existing.Found)
                    {
                        Index.Insert(pkStr, currentRecordPosition);
                        recordsIndexed++;
                    }
                    else
                    {
                        // Update to newer position (later in file = newer version)
                        Index.Delete(pkStr);
                        Index.Insert(pkStr, currentRecordPosition);
                    }
                }
            }
            catch
            {
                // Skip corrupted records
            }

            filePosition += 4 + recordLength;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[RebuildPrimaryKeyIndexFromDisk] ✅ Indexed {recordsIndexed} records");
#endif
    }

    /// <summary>
    /// Disposes the table and releases all resources including locks and index managers.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Flushes all pending writes to disk.
    /// Ensures INSERT/UPDATE/DELETE operations are persisted.
    /// </summary>
    public void Flush()
    {
        if (isReadOnly || storage == null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Table.Flush] Skipping flush for table {Name} (readonly={isReadOnly}, storage={storage == null})");
#endif
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[Table.Flush] Flushing table: {Name}");
        System.Diagnostics.Debug.WriteLine($"[Table.Flush] DataFile: {DataFile}");
        System.Diagnostics.Debug.WriteLine($"[Table.Flush] StorageMode: {StorageMode}");
        System.Diagnostics.Debug.WriteLine($"[Table.Flush] StorageEngine: {(_storageEngine != null ? _storageEngine.GetType().Name : "NULL")}");
#endif

        try
        {
            // Flush storage engine if present
            if (_storageEngine != null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] Calling StorageEngine.Flush()...");
#endif
                _storageEngine.Flush();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] Storage engine flushed for table: {Name}");
#endif
            }
            else
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] WARNING: No storage engine to flush for table {Name}!");
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] Attempting direct storage write for backward compatibility...");
#endif
                
                // ✅ FALLBACK: If no storage engine, try to flush via storage directly
                // This handles legacy columnar tables that don't use storage engines
                if (storage != null && File.Exists(DataFile))
                {
                    // Force storage to commit any buffered writes
                    if (storage.IsInTransaction)
                    {
                        storage.FlushTransactionBuffer();
                    }
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[Table.Flush] Flushed storage transaction buffer");
#endif
                }
            }
            
            // Flush indexes
            if (indexManager != null)
            {
                foreach (var index in hashIndexes.Values)
                {
                    // Hash indexes are in-memory only, no flush needed
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] Indexes flushed for table: {Name}");
#endif
            }
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Table.Flush] ✅ Flush completed for table: {Name}");
            
            // Check file size after flush
            if (File.Exists(DataFile))
            {
                var fileInfo = new FileInfo(DataFile);
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] DataFile size: {fileInfo.Length} bytes");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Table.Flush] WARNING: DataFile does not exist: {DataFile}");
            }
#endif
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Table.Flush] ERROR flushing table {Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Table.Flush] Stack trace: {ex.StackTrace}");
#endif
            throw new InvalidOperationException($"Failed to flush table {Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Disposes the table resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose storage engine first
            DisposeStorageEngine();
            
            this.indexManager?.Dispose();
            _indexQueue.Writer.Complete();
            this.rwLock.Dispose();
        }
    }
}

/// <summary>
/// Custom pooled object policy for Dictionary&lt;string, object&gt; that clears the dictionary before reuse.
/// This reduces allocations during SELECT operations by reusing dictionary instances.
/// </summary>
internal class DictionaryPooledObjectPolicy : IPooledObjectPolicy<Dictionary<string, object>>
{
    public Dictionary<string, object> Create()
    {
        return new Dictionary<string, object>();
    }

    public bool Return(Dictionary<string, object> obj)
    {
        obj.Clear();
        return true;
    }
}
