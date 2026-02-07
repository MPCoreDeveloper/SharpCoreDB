using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Connection string builder for SharpCoreDB.
/// Supported properties: Path (Data Source), Password, ReadOnly, Cache.
/// </summary>
public sealed class SharpCoreDBConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string PathKey = "Path";
    private const string PasswordKey = "Password";
    private const string DataSourceKey = "Data Source";
    private const string ReadOnlyKey = "ReadOnly";
    private const string CacheKey = "Cache";

    /// <summary>
    /// Gets or sets the database file path.
    /// </summary>
    public string? Path
    {
        get
        {
            if (ContainsKey(PathKey))
                return this[PathKey]?.ToString();
            if (ContainsKey(DataSourceKey))
                return this[DataSourceKey]?.ToString();
            return null;
        }
        set => this[PathKey] = value;
    }

    /// <summary>
    /// Gets or sets the master password for the database.
    /// </summary>
    public string? Password
    {
        get => ContainsKey(PasswordKey) ? this[PasswordKey]?.ToString() : null;
        set => this[PasswordKey] = value;
    }

    /// <summary>
    /// Gets or sets the data source (alias for Path).
    /// </summary>
    public string? DataSource
    {
        get => Path;
        set => Path = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the database should be opened in read-only mode.
    /// </summary>
    public bool ReadOnly
    {
        get => ContainsKey(ReadOnlyKey) && bool.TryParse(this[ReadOnlyKey]?.ToString(), out var ro) && ro;
        set => this[ReadOnlyKey] = value;
    }

    /// <summary>
    /// Gets or sets the cache mode (Shared or Private).
    /// </summary>
    public string Cache
    {
        get => ContainsKey(CacheKey) ? this[CacheKey]?.ToString() ?? "Private" : "Private";
        set => this[CacheKey] = value;
    }
}
