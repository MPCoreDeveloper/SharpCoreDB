namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Text;
using System.Buffers;
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
    public Table() { }
    
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

    private readonly Dictionary<string, HashIndex> hashIndexes = [];
    private readonly Dictionary<string, IndexMetadata> registeredIndexes = [];
    private readonly HashSet<string> loadedIndexes = [];
    private readonly HashSet<string> staleIndexes = [];
    
    // 🔥 CRITICAL FIX: Map index NAMES to COLUMN names for proper DROP INDEX support
    // SQL: CREATE INDEX idx_email ON users(email) 
    //      → indexNameToColumn["idx_email"] = "email"
    // SQL: DROP INDEX idx_email
    //      → lookup "idx_email" → find "email" → remove hash index on "email"
    private readonly Dictionary<string, string> indexNameToColumn = [];
    
    private IStorage? storage;
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

    /// <summary>
    /// Sets the storage instance for this table.
    /// </summary>
    /// <param name="storage">The storage instance.</param>
    public void SetStorage(IStorage storage) => this.storage = storage;
    
    /// <summary>
    /// Sets the readonly flag for this table.
    /// </summary>
    /// <param name="isReadOnly">True if readonly.</param>
    public void SetReadOnly(bool isReadOnly) => this.isReadOnly = isReadOnly;

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
    /// Disposes the table and releases all resources including locks and index managers.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
