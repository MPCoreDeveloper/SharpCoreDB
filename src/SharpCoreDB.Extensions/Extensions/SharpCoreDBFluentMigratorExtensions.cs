using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators;
using FluentMigrator.Runner.Processors;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Extensions.Processor;
using SharpCoreDB.Extensions.Runner;

namespace SharpCoreDB.Extensions.Extensions;

/// <summary>
/// Extension methods for integrating FluentMigrator with SharpCoreDB.
/// </summary>
public static class SharpCoreDBFluentMigratorExtensions
{
    /// <summary>
    /// Registers FluentMigrator with the SharpCoreDB custom processor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional FluentMigrator runner configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpCoreDBFluentMigrator(
        this IServiceCollection services,
        Action<IMigrationRunnerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<SharpCoreDbMigrationExecutor>();
        services.TryAddScoped<SharpCoreDbProcessorFactory>();
        services.TryAddScoped<IMigrationProcessor>(sp => sp.GetRequiredService<SharpCoreDbProcessorFactory>().Create());
        services.TryAddScoped<ISharpCoreDbMigrationRunner, FluentMigratorSharpRunner>();
        services.TryAddSingleton<IVersionTableMetaData, SharpCoreDbVersionTableMetaData>();

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                runner.AddSharpCoreDbProcessor();
                runner.WithVersionTable(new SharpCoreDbVersionTableMetaData());
                configure?.Invoke(runner);
            });

        return services;
    }

    /// <summary>
    /// Registers FluentMigrator with SharpCoreDB remote gRPC execution.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SharpCoreDB.Client connection string.</param>
    /// <param name="configure">Optional FluentMigrator runner configuration.</param>
    /// <param name="configureGrpc">Optional gRPC migration options configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpCoreDBFluentMigratorGrpc(
        this IServiceCollection services,
        string connectionString,
        Action<IMigrationRunnerBuilder>? configure = null,
        Action<SharpCoreDbGrpcMigrationOptions>? configureGrpc = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSharpCoreDBFluentMigrator(configure);

        var options = new SharpCoreDbGrpcMigrationOptions(connectionString);
        configureGrpc?.Invoke(options);

        services.AddScoped<ISharpCoreDbMigrationSqlExecutor>(_ =>
            new SharpCoreDbGrpcMigrationSqlExecutor(options));

        return services;
    }

    /// <summary>
    /// Registers the SharpCoreDB processor in the FluentMigrator runner pipeline.
    /// </summary>
    /// <param name="builder">The migration runner builder.</param>
    /// <returns>The migration runner builder.</returns>
    public static IMigrationRunnerBuilder AddSharpCoreDbProcessor(this IMigrationRunnerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<SharpCoreDbMigrationExecutor>();
        builder.Services.TryAddScoped<SharpCoreDbProcessorFactory>();
        builder.Services.TryAddScoped<IMigrationProcessor>(sp => sp.GetRequiredService<SharpCoreDbProcessorFactory>().Create());

        builder.Services.PostConfigure<SelectingProcessorAccessorOptions>(options =>
        {
            options.ProcessorId = SharpCoreDbProcessorFactory.ProviderId;
        });

        builder.Services.PostConfigure<SelectingGeneratorAccessorOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.GeneratorId))
            {
                options.GeneratorId = "sqlite";
            }
        });

        builder.ConfigureGlobalProcessorOptions(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                options.ConnectionString = "sharpcoredb://embedded";
            }

            if (string.IsNullOrWhiteSpace(options.ProviderSwitches))
            {
                options.ProviderSwitches = "syntax=sqlite";
            }
        });

        return builder;
    }
}
