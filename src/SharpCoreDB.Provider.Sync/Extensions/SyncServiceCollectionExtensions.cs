#nullable enable

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Provider.Sync.ChangeTracking;
using SharpCoreDB.Services;

namespace SharpCoreDB.Provider.Sync.Extensions;

/// <summary>
/// Service collection extensions for registering SharpCoreDB.Provider.Sync in dependency injection containers.
/// Follows the Microsoft.Extensions.DependencyInjection pattern established by SharpCoreDB.Provider.YesSql.
/// </summary>
/// <remarks>
/// **Usage:**
/// <code>
/// services.AddSharpCoreDBSync(
///     connectionString: "Path=C:\data\local.scdb;Password=secret",
///     options: opts => {
///         opts.EnableAutoTracking = true;
///         opts.TombstoneRetentionDays = 30;
///     }
/// );
/// </code>
/// </remarks>
public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Adds SharpCoreDB Dotmim.Sync provider to the service collection.
    /// Registers all required services with singleton lifetime.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="connectionString">SharpCoreDB connection string (e.g., "Path=C:\data\local.scdb;Password=secret")</param>
    /// <param name="configure">Optional configuration callback for sync options</param>
    /// <returns>The service collection for method chaining</returns>
    /// <exception cref="ArgumentNullException">If services or connectionString is null</exception>
    /// <exception cref="ArgumentException">If connectionString is empty or whitespace</exception>
    public static IServiceCollection AddSharpCoreDBSync(
        this IServiceCollection services,
        string connectionString,
        Action<SyncProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNullOrEmpty(connectionString);

        // Create and configure options
        var options = new SyncProviderOptions();
        configure?.Invoke(options);

        // Register configuration singleton
        services.AddSingleton(options);

        // Register provider factory
        services.AddSingleton<SyncProviderFactory>();

        // Register provider instance created via factory
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<SyncProviderFactory>();
            return factory.CreateProvider(connectionString, options);
        });

        // Register change tracking services
        services.AddSingleton<SqliteDialect>();
        services.AddSingleton<TrackingTableBuilder>();
        services.AddSingleton<IChangeTrackingManager, ChangeTrackingManager>();
        services.AddSingleton<ITombstoneManager, TombstoneManager>();

        return services;
    }
}
