namespace SharpCoreDB.Constants;

/// <summary>
/// Constants for database persistence.
/// </summary>
public static class PersistenceConstants
{
    /// <summary>The name of the metadata file.</summary>
    public const string MetaFileName = "meta.json";
    /// <summary>The name of the write-ahead log file.</summary>
    public const string WalFileName = "wal.log";
    /// <summary>The file extension for table files.</summary>
    public const string TableFileExtension = ".json";
    /// <summary>The key for tables in metadata.</summary>
    public const string TablesKey = "tables";
}
