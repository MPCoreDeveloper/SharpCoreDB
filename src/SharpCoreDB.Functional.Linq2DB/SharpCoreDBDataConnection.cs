using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// A linq2db <see cref="DataConnection"/> over a SharpCoreDB database.
/// <para>
/// linq2db generates the SQL with its SQLite dialect — SharpCoreDB is SQLite-compatible at the SQL level — and
/// executes it through <see cref="SharpCoreDBConnection"/>, so the rows are stored by SharpCoreDB, encrypted with
/// AES-256-GCM when the connection string carries a password, and no SQLite file is created.
/// </para>
/// <para>
/// Up to 2.1.2 this class passed the connection string to <c>ProviderName.SQLite</c> instead, which resolved to
/// Microsoft.Data.Sqlite and the native <c>e_sqlite3</c> library: a password was refused ("the native SQLite
/// library doesn't support encryption") and without one the data landed in an unencrypted
/// <c>SQLite format 3</c> file that SharpCoreDB cannot open. The adapter is the SQLite <em>dialect</em>, not the
/// SQLite <em>engine</em>.
/// </para>
/// </summary>
public sealed class SharpCoreDBDataConnection : DataConnection
{
    /// <summary>
    /// Initializes a connection to the SharpCoreDB database named by <paramref name="connectionString"/>
    /// (<c>Data Source=./app.scdb;Password=…</c>, or <c>Path=…</c>).
    /// linq2db's SQLite dialect generates the SQL; SharpCoreDB executes it and owns the file.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    public SharpCoreDBDataConnection(string connectionString)
        : base(CreateOptions(connectionString))
    {
        EnsureConnectionOpen();
    }

    /// <summary>
    /// Initializes using pre-built <see cref="DataOptions"/> (recommended for DI). The options must carry a
    /// SharpCoreDB connection — build them with <see cref="CreateOptions"/> or
    /// <see cref="SharpCoreDBProviderExtensions.CreateSharpCoreDBOptions"/>.
    /// </summary>
    /// <param name="options">linq2db options, already bound to a SharpCoreDB connection.</param>
    public SharpCoreDBDataConnection(DataOptions options)
        : base(options.UseMappingSchema(SharpCoreDBMappingSchema.Instance))
    {
        EnsureConnectionOpen();
    }

    /// <summary>
    /// Builds <see cref="DataOptions"/> that pair linq2db's SQLite SQL dialect with a SharpCoreDB ADO.NET
    /// connection. This is the one place the pairing is defined; the string constructor,
    /// <see cref="SharpCoreDBProviderExtensions.CreateSharpCoreDBOptions"/> and
    /// <see cref="Extensions.UseSharpCoreDB"/> all route through it.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>Options ready for <see cref="SharpCoreDBDataConnection"/>.</returns>
    public static DataOptions CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var (provider, connection) = CreateProviderAndConnection(connectionString);
        return Bind(new DataOptions(), provider, connection);
    }

    /// <summary>
    /// Creates the SharpCoreDB connection and the linq2db provider that generates SQL for it — the one place the
    /// pairing is defined.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>The SQLite-dialect provider and the SharpCoreDB connection it will execute on.</returns>
    internal static (LinqToDB.DataProvider.IDataProvider Provider, SharpCoreDBConnection Connection) CreateProviderAndConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var connection = new SharpCoreDBConnection(connectionString);

        // The provider name selects the SQL *dialect*; because a live connection is supplied, linq2db executes
        // every command on that connection instead of creating one of its own.
        var provider = SQLiteTools.GetDataProvider(SQLiteProvider.Microsoft, connectionString, connection);
        return (provider, connection);
    }

    /// <summary>
    /// Binds a provider and connection to <paramref name="options"/> together with the settings the adapter
    /// requires (multi-row SQL bulk copy, SharpCoreDB type mappings).
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <param name="provider">The SQLite-dialect provider.</param>
    /// <param name="connection">The SharpCoreDB connection.</param>
    /// <returns>The extended options.</returns>
    internal static DataOptions Bind(DataOptions options, LinqToDB.DataProvider.IDataProvider provider, SharpCoreDBConnection connection)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseConnection(provider, connection)
            // Provider-specific SQLite bulk copy needs a native Microsoft.Data.Sqlite connection; generate the
            // inserts as multi-row SQL instead so bulk copy works through SharpCoreDB.
            .UseBulkCopyType(BulkCopyType.MultipleRows)
            .UseMappingSchema(SharpCoreDBMappingSchema.Instance);
    }

    /// <summary>
    /// Gets a table reference for LINQ queries.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <returns>The linq2db table.</returns>
    public ITable<T> GetTable<T>() where T : class => LinqToDB.DataExtensions.GetTable<T>(this);

    private void EnsureConnectionOpen()
    {
        // Open eagerly so GetUnderlyingConnection().CreateCommand() works immediately, which is how callers add
        // DDL that linq2db does not model.
        if (Connection?.State != System.Data.ConnectionState.Open)
        {
            Connection?.Open();
        }
    }
}
