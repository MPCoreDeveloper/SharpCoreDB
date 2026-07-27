using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Extension methods for SharpCoreDB linq2db integration (linq2db 6+).
/// C# 14: Clean fluent APIs.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Creates DataOptions configured for SharpCoreDB.
    /// </summary>
    public static DataOptions UseSharpCoreDB(this DataOptions options, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return options
            .UseConnectionString(ProviderName.SQLite, connectionString)
            .UseMappingSchema(SharpCoreDBMappingSchema.Instance);
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
    /// Executes a query and returns the underlying SharpCoreDB database instance.
    /// Useful for accessing native SharpCoreDB features not exposed through linq2db.
    /// </summary>
    /// <param name="connection">The data connection</param>
    /// <returns>The underlying SharpCoreDB connection</returns>
    public static SharpCoreDB.Data.Provider.SharpCoreDBConnection GetUnderlyingConnection(
        this SharpCoreDBDataConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return (SharpCoreDB.Data.Provider.SharpCoreDBConnection)connection.Connection;
    }
}
