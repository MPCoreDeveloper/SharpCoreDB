
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Microsoft.Extensions.DependencyInjection;
namespace SharpCoreDB.Serilog.Sinks;
/// Michel Posseth 
/// <summary>
/// Extension methods for configuring the SharpCoreDB sink with Serilog.
/// </summary>
public static class LoggerConfigurationExtensions
{
    private const int DefaultBatchPostingLimit = 50;
    private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Writes log events to a SharpCoreDB database using the provided database instance.
    /// </summary>
    /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
    /// <param name="database">The SharpCoreDB database instance to write to.</param>
    /// <param name="tableName">The name of the table to write logs to (default: "Logs").</param>
    /// <param name="restrictedToMinimumLevel">The minimum log level (default: Verbose).</param>
    /// <param name="batchPostingLimit">The maximum number of events to include in a single batch (default: 50).</param>
    /// <param name="period">The time to wait between checking for event batches (default: 2 seconds).</param>
    /// <param name="autoCreateTable">Whether to automatically create the table if it doesn't exist (default: true).</param>
    /// <param name="storageEngine">The storage engine to use (default: "AppendOnly").</param>
    /// <returns>Logger configuration, allowing chaining.</returns>
    public static LoggerConfiguration SharpCoreDB(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        IDatabase database,
        string tableName = "Logs",
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        int batchPostingLimit = DefaultBatchPostingLimit,
        TimeSpan? period = null,
        bool autoCreateTable = true,
        string storageEngine = "AppendOnly")
    {
        ArgumentNullException.ThrowIfNull(loggerSinkConfiguration);

        ArgumentNullException.ThrowIfNull(database);

        var actualPeriod = period ?? DefaultPeriod;

        var sink = new SharpCoreDBSink(
            database,
            tableName,
            autoCreateTable,
            storageEngine);

        var batchingOptions = new PeriodicBatchingSinkOptions
        {
            BatchSizeLimit = batchPostingLimit,
            Period = actualPeriod,
            EagerlyEmitFirstEvent = true,
            QueueLimit = 10000
        };

        var batchingSink = new PeriodicBatchingSink(sink, batchingOptions);

        return loggerSinkConfiguration.Sink(batchingSink, restrictedToMinimumLevel);
    }

    /// <summary>
    /// Writes log events to a SharpCoreDB database using a connection string.
    /// </summary>
    /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
    /// <param name="path">The path to the .scdb file.</param>
    /// <param name="password">The encryption password for the database.</param>
    /// <param name="tableName">The name of the table to write logs to (default: "Logs").</param>
    /// <param name="restrictedToMinimumLevel">The minimum log level (default: Verbose).</param>
    /// <param name="batchPostingLimit">The maximum number of events to include in a single batch (default: 50).</param>
    /// <param name="period">The time to wait between checking for event batches (default: 2 seconds).</param>
    /// <param name="autoCreateTable">Whether to automatically create the table if it doesn't exist (default: true).</param>
    /// <param name="storageEngine">The storage engine to use (default: "AppendOnly").</param>
    /// <param name="serviceProvider">Optional service provider for resolving dependencies. If null, creates a new instance.</param>
    /// <returns>Logger configuration, allowing chaining.</returns>
    public static LoggerConfiguration SharpCoreDB(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        string path,
        string password,
        string tableName = "Logs",
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        int batchPostingLimit = DefaultBatchPostingLimit,
        TimeSpan? period = null,
        bool autoCreateTable = true,
        string storageEngine = "AppendOnly",
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(loggerSinkConfiguration);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be null or whitespace.", nameof(password));
        }

        // Create or use provided service provider
        var services = serviceProvider ?? CreateDefaultServiceProvider();
        var factory = services.GetRequiredService<DatabaseFactory>();
        
        // Create database with optimized config for logging
        var config = new DatabaseConfig
        {
            EnableQueryCache = false, // Logs are typically write-once
            EnablePageCache = true,
            PageCacheCapacity = 1024,
            UseGroupCommitWal = true,
            WalDurabilityMode = DurabilityMode.Async,
            WalMaxBatchSize = batchPostingLimit,
            WalMaxBatchDelayMs = (int)period.GetValueOrDefault(DefaultPeriod).TotalMilliseconds
        };

        var database = factory.Create(path, password, isReadOnly: false, config: config);

        return SharpCoreDB(
            loggerSinkConfiguration,
            database,
            tableName,
            restrictedToMinimumLevel,
            batchPostingLimit,
            period,
            autoCreateTable,
            storageEngine);
    }

    /// <summary>
    /// Writes log events to a SharpCoreDB database using options.
    /// </summary>
    /// <param name="loggerSinkConfiguration">The logger sink configuration.</param>
    /// <param name="options">The sink options.</param>
    /// <returns>Logger configuration, allowing chaining.</returns>
    public static LoggerConfiguration SharpCoreDB(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        SharpCoreDBSinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(loggerSinkConfiguration);

        ArgumentNullException.ThrowIfNull(options);

        if (options.Database != null)
        {
            return SharpCoreDB(
                loggerSinkConfiguration,
                options.Database,
                options.TableName,
                options.RestrictedToMinimumLevel,
                options.BatchPostingLimit,
                options.Period,
                options.AutoCreateTable,
                options.StorageEngine);
        }

        if (!string.IsNullOrWhiteSpace(options.Path))
        {
            return SharpCoreDB(
                loggerSinkConfiguration,
                options.Path,
                options.Password ?? string.Empty,
                options.TableName,
                options.RestrictedToMinimumLevel,
                options.BatchPostingLimit,
                options.Period,
                options.AutoCreateTable,
                options.StorageEngine,
                options.ServiceProvider);
        }

        throw new ArgumentException("Either Database or Path must be specified in options.", nameof(options));
    }

    private static IServiceProvider CreateDefaultServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        return services.BuildServiceProvider();
    }
}
