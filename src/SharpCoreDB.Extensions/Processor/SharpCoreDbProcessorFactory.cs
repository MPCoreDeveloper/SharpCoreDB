using FluentMigrator;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.Options;

namespace SharpCoreDB.Extensions.Processor;

/// <summary>
/// Factory for creating SharpCoreDB migration processors.
/// </summary>
public sealed class SharpCoreDbProcessorFactory(
    IConnectionStringAccessor connectionStringAccessor,
    IOptionsSnapshot<ProcessorOptions> processorOptions,
    SharpCoreDbMigrationExecutor executor)
{
    /// <summary>
    /// The FluentMigrator provider id used by this processor.
    /// </summary>
    public const string ProviderId = "sharpcoredb";

    private readonly IConnectionStringAccessor _connectionStringAccessor = connectionStringAccessor;
    private readonly IOptionsSnapshot<ProcessorOptions> _processorOptions = processorOptions;
    private readonly SharpCoreDbMigrationExecutor _executor = executor;

    /// <summary>
    /// Creates a configured <see cref="IMigrationProcessor"/> instance for SharpCoreDB.
    /// </summary>
    /// <returns>The migration processor.</returns>
    public IMigrationProcessor Create()
    {
        var connectionString = _connectionStringAccessor.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "sharpcoredb://embedded";
        }

        var processor = new SharpCoreDbProcessor(connectionString, _processorOptions.Value, _executor);
        // Ensure the version table exists so FluentMigrator can record applied migrations.
        // This is idempotent (CREATE TABLE IF NOT EXISTS) and safe to run on every processor creation.
        SharpCoreDbMigrationExecutor.EnsureVersionTable(_executor);
        return processor;
    }
}
