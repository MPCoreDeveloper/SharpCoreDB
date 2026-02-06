using Serilog.Events;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Serilog.Sinks;

/// <summary>
/// Configuration options for the SharpCoreDB Serilog sink.
/// All properties have sensible defaults for high-performance logging.
/// </summary>
public class SharpCoreDBSinkOptions
{
    /// <summary>
    /// Gets or sets the SharpCoreDB database instance to write to.
    /// If set, this takes precedence over <see cref="Path"/>/<see cref="Password"/>.
    /// </summary>
    public IDatabase? Database { get; set; }

    /// <summary>
    /// Gets or sets the path to the .scdb file.
    /// Used only if <see cref="Database"/> is not set.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the encryption password for the database.
    /// Used only if <see cref="Database"/> is not set.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the name of the table to write logs to.
    /// Default: "Logs"
    /// </summary>
    public string TableName { get; set; } = "Logs";

    /// <summary>
    /// Gets or sets the minimum log level.
    /// Default: Verbose
    /// </summary>
    public LogEventLevel RestrictedToMinimumLevel { get; set; } = LogEventLevel.Verbose;

    /// <summary>
    /// Gets or sets the maximum number of events to include in a single batch.
    /// Default: 50. Increase for high-volume scenarios (e.g., 500-1000).
    /// </summary>
    public int BatchPostingLimit { get; set; } = 50;

    /// <summary>
    /// Gets or sets the time to wait between checking for event batches.
    /// Default: 2 seconds. Decrease for lower latency (e.g., 500ms).
    /// </summary>
    public TimeSpan? Period { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets whether to automatically create the table if it doesn't exist.
    /// Default: true
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>
    /// Gets or sets the storage engine to use.
    /// Options: "AppendOnly" (fastest writes), "PageBased" (read/write balance), "Columnar" (analytics).
    /// Default: "AppendOnly" — optimal for high-volume write-once log workloads.
    /// </summary>
    public string StorageEngine { get; set; } = "AppendOnly";

    /// <summary>
    /// Gets or sets the service provider for dependency injection.
    /// Used only if <see cref="Database"/> is not set and <see cref="Path"/> is provided.
    /// If null, a default service provider will be created.
    /// </summary>
    public IServiceProvider? ServiceProvider { get; set; }
}
