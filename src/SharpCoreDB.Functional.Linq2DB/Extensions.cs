using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Extension methods for SharpCoreDB linq2db integration (linq2db 6+).
/// C# 15: Clean fluent APIs.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Creates DataOptions configured for SharpCoreDB: linq2db's SQLite dialect over a SharpCoreDB connection.
    /// </summary>
    /// <param name="options">The options to extend.</param>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>The extended options.</returns>
    public static DataOptions UseSharpCoreDB(this DataOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var (provider, connection) = SharpCoreDBDataConnection.CreateProviderAndConnection(connectionString);
        return SharpCoreDBDataConnection.Bind(options, provider, connection);
    }

    /// <summary>
    /// Adds SharpCoreDB linq2db DataConnection (via DataOptions) to the service collection.
    /// </summary>
    public static IServiceCollection AddSharpCoreDBLinq2Db(
        this IServiceCollection services,
        string connectionString,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new DataOptions()
            .UseSharpCoreDB(connectionString);

        services.Add(new ServiceDescriptor(
            typeof(SharpCoreDBDataConnection),
            _ => new SharpCoreDBDataConnection(options),
            lifetime));

        return services;
    }

    /// <summary>
    /// Adds SharpCoreDB DataConnection with custom DataOptions configuration.
    /// </summary>
    public static IServiceCollection AddSharpCoreDBLinq2Db(
        this IServiceCollection services,
        Action<DataOptions> configureOptions,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new DataOptions();
        configureOptions(options);

        services.Add(new ServiceDescriptor(
            typeof(SharpCoreDBDataConnection),
            _ => new SharpCoreDBDataConnection(options),
            lifetime));

        return services;
    }

    /// <summary>
    /// Creates a SharpCoreDB DataConnection with fluent configuration.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string</param>
    /// <returns>Configured DataConnection instance</returns>
    public static SharpCoreDBDataConnection CreateSharpCoreDBConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new SharpCoreDBDataConnection(connectionString);
    }

    /// <summary>
    /// Gets the underlying SharpCoreDB ADO.NET connection — a <see cref="SharpCoreDB.Data.Provider.SharpCoreDBConnection"/>,
    /// not a SQLite one — for DDL and features linq2db does not model.
    /// </summary>
    /// <param name="connection">The data connection.</param>
    /// <returns>The underlying SharpCoreDB connection.</returns>
    public static System.Data.Common.DbConnection GetUnderlyingConnection(
        this SharpCoreDBDataConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // linq2db v6+ recommends TryGetDbConnection/OpenDbConnection; .Connection is what the adapter opens
        // eagerly, so return it as-is. It used to close it to release a Windows SQLite file handle — there is no
        // SQLite handle any more, and closing it would make the next command fail.
        return connection.Connection;
    }
}
