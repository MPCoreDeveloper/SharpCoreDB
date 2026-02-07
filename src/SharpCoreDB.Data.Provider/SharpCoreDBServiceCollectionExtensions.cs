using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Extension methods for registering SharpCoreDB ADO.NET Data Provider in dependency injection.
/// </summary>
public static class SharpCoreDBServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SharpCoreDBProviderFactory"/> as the <see cref="DbProviderFactory"/>
    /// in the service collection and registers it with <see cref="DbProviderFactories"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharpCoreDBDataProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        DbProviderFactories.RegisterFactory(
            "SharpCoreDB.Data.Provider",
            SharpCoreDBProviderFactory.Instance);

        services.AddSingleton<DbProviderFactory>(SharpCoreDBProviderFactory.Instance);

        return services;
    }

    /// <summary>
    /// Registers <see cref="SharpCoreDBProviderFactory"/> and configures a default
    /// <see cref="SharpCoreDBConnection"/> with the specified connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The default connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharpCoreDBDataProvider(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSharpCoreDBDataProvider();

        services.AddTransient<SharpCoreDBConnection>(_ =>
            new SharpCoreDBConnection(connectionString));

        services.AddTransient<DbConnection>(sp =>
            sp.GetRequiredService<SharpCoreDBConnection>());

        return services;
    }
}
