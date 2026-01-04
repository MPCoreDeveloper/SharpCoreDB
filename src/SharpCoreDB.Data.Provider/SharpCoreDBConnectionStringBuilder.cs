using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Connection string builder for SharpCoreDB.
/// Supported properties: Path, Password
/// </summary>
public sealed class SharpCoreDBConnectionStringBuilder : DbConnectionStringBuilder
{
    private const string PathKey = "Path";
    private const string PasswordKey = "Password";
    private const string DataSourceKey = "Data Source";

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
}
