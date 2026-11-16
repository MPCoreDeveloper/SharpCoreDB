using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.Mapping;
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
    /// SharpCoreDB connection — build them with <see cref="CreateOptions"/>,
    /// <see cref="SharpCoreDBProviderExtensions.CreateSharpCoreDBOptions"/> or <see cref="Extensions.UseSharpCoreDB"/>.
    /// A mapping schema already on the options is kept and takes priority over the SharpCoreDB type mappings.
    /// </summary>
    /// <param name="options">linq2db options, already bound to a SharpCoreDB connection.</param>
    public SharpCoreDBDataConnection(DataOptions options)
        : base(WithSharpCoreDBMappingSchema(options))
    {
        EnsureConnectionOpen();
    }

    /// <summary>
    /// Builds <see cref="DataOptions"/> that pair linq2db's SQLite SQL dialect with SharpCoreDB ADO.NET
    /// connections. This is the one place the pairing is defined; the string constructor,
    /// <see cref="SharpCoreDBProviderExtensions.CreateSharpCoreDBOptions"/> and
    /// <see cref="Extensions.UseSharpCoreDB"/> all route through it.
    /// <para>
    /// The options hold a connection <em>factory</em>, not a connection: every <see cref="SharpCoreDBDataConnection"/>
    /// built from them opens its own <see cref="SharpCoreDBConnection"/> and disposes it with itself, so the options
    /// can be reused — by a DI registration, for example — without sharing one ADO.NET connection between callers.
    /// </para>
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>Options ready for <see cref="SharpCoreDBDataConnection"/>.</returns>
    public static DataOptions CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return Bind(new DataOptions(), connectionString);
    }

    /// <summary>
    /// Binds the SQLite-dialect provider and a SharpCoreDB connection factory to <paramref name="options"/>,
    /// together with the settings the adapter requires (multi-row SQL bulk copy, SharpCoreDB type mappings).
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>The extended options.</returns>
    internal static DataOptions Bind(DataOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // The provider selects the SQL *dialect* only. Because linq2db takes its connections from the factory, it
        // executes every command on a SharpCoreDBConnection and never opens Microsoft.Data.Sqlite; a connection it
        // created through the factory is one it owns and disposes. Up to 2.1.4 the options captured one live
        // connection instead, which every DataConnection built from them — every DI scope — then shared.
        var provider = SQLiteTools.GetDataProvider(SQLiteProvider.Microsoft);

        return WithSharpCoreDBMappingSchema(options
            .UseConnectionFactory(provider, _ => new SharpCoreDBConnection(connectionString))
            // Provider-specific SQLite bulk copy needs a native Microsoft.Data.Sqlite connection; generate the
            // inserts as multi-row SQL instead so bulk copy works through SharpCoreDB.
            .UseBulkCopyType(BulkCopyType.MultipleRows));
    }

    /// <summary>
    /// Adds <see cref="SharpCoreDBMappingSchema"/> to <paramref name="options"/> without discarding a mapping schema
    /// the caller registered. <c>UseMappingSchema</c> alone "replaces all previous registrations", which up to 2.1.4
    /// silently dropped fluent mappings. The caller's schema goes first because linq2db gives the first base schema
    /// the higher priority.
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <returns>Options whose mapping schema includes the SharpCoreDB type mappings exactly once.</returns>
    internal static DataOptions WithSharpCoreDBMappingSchema(DataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var current = options.ConnectionOptions.MappingSchema;
        if (current is null)
        {
            return options.UseMappingSchema(SharpCoreDBMappingSchema.Instance);
        }

        if (current.ConfigurationList.Contains(SharpCoreDBMappingSchema.ConfigurationName))
        {
            return options;
        }

        return options.UseMappingSchema(new MappingSchema(current, SharpCoreDBMappingSchema.Instance));
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
