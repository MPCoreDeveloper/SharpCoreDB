using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Provider factory for SharpCoreDB ADO.NET Data Provider.
/// Allows usage in SQL Server Management Studio and other ADO.NET consumers.
/// </summary>
public sealed class SharpCoreDBProviderFactory : DbProviderFactory
{
    /// <summary>
    /// Singleton instance of the provider factory.
    /// </summary>
    public static readonly SharpCoreDBProviderFactory Instance = new();

    private SharpCoreDBProviderFactory()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the provider supports <see cref="DbDataSourceEnumerator"/>.
    /// </summary>
    public override bool CanCreateDataSourceEnumerator => false;

    /// <summary>
    /// Creates a new <see cref="DbCommand"/> instance.
    /// </summary>
    public override DbCommand? CreateCommand()
    {
        return new SharpCoreDBCommand();
    }

    /// <summary>
    /// Creates a new <see cref="DbConnection"/> instance.
    /// </summary>
    public override DbConnection? CreateConnection()
    {
        return new SharpCoreDBConnection();
    }

    /// <summary>
    /// Creates a new <see cref="DbConnectionStringBuilder"/> instance.
    /// </summary>
    public override DbConnectionStringBuilder? CreateConnectionStringBuilder()
    {
        return new SharpCoreDBConnectionStringBuilder();
    }

    /// <summary>
    /// Creates a new <see cref="DbParameter"/> instance.
    /// </summary>
    public override DbParameter? CreateParameter()
    {
        return new SharpCoreDBParameter();
    }

    /// <summary>
    /// Creates a new <see cref="DbDataAdapter"/> instance.
    /// </summary>
    public override DbDataAdapter? CreateDataAdapter()
    {
        return new SharpCoreDBDataAdapter();
    }

    /// <summary>
    /// Creates a new <see cref="DbCommandBuilder"/> instance.
    /// </summary>
    public override DbCommandBuilder? CreateCommandBuilder()
    {
        return new SharpCoreDBCommandBuilder();
    }
}
