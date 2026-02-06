using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using SharpCoreDB.Interfaces;
using System.Text;

namespace SharpCoreDB.Serilog.Sinks;

/// <summary>
/// Serilog sink that writes log events to a SharpCoreDB database.
/// C# 14: Uses ExecuteBatchSQLAsync which routes through InsertBatch for optimal storage engine throughput.
/// Follows zero-allocation principles in hot paths where possible.
/// </summary>
public sealed class SharpCoreDBSink : IBatchedLogEventSink, IDisposable
{
    private readonly IDatabase _database;
    private readonly string _tableName;
    private readonly string _storageEngine;
    private readonly string _insertPrefix;
    private readonly Lock _initLock = new();
    private bool _tableCreated;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBSink"/> class.
    /// </summary>
    /// <param name="database">The SharpCoreDB database instance.</param>
    /// <param name="tableName">The name of the table to write logs to.</param>
    /// <param name="autoCreateTable">Whether to automatically create the table if it doesn't exist.</param>
    /// <param name="storageEngine">The storage engine to use (default: AppendOnly).</param>
    public SharpCoreDBSink(
        IDatabase database,
        string tableName = "Logs",
        bool autoCreateTable = true,
        string storageEngine = "AppendOnly")
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        _database = database;
        _tableName = tableName;
        _storageEngine = storageEngine ?? "AppendOnly";

        // PERF: Cache the INSERT prefix — table name never changes after construction
        _insertPrefix = $"INSERT INTO {_tableName} (Timestamp, Level, Message, Exception, Properties) VALUES (";

        if (autoCreateTable)
        {
            EnsureTableCreated();
        }
    }

    /// <summary>
    /// Emits a batch of log events to the database.
    /// C# 14: Uses ExecuteBatchSQLAsync which internally routes INSERT statements
    /// through InsertBatch for optimal storage engine throughput.
    /// </summary>
    /// <param name="batch">The batch of log events to write.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // PERF: Build all INSERT statements and batch them via ExecuteBatchSQLAsync
        // This internally uses InsertBatch for direct storage engine writes (coding standard compliant)
        List<string> statements = [];

        foreach (var logEvent in batch)
        {
            statements.Add(BuildInsertSql(logEvent));
        }

        if (statements.Count > 0)
        {
            await _database.ExecuteBatchSQLAsync(statements).ConfigureAwait(false);

            // Per coding standards: Always flush after writes to ensure data persistence
            _database.Flush();
        }
    }

    /// <summary>
    /// Called when an empty batch is detected.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds a complete INSERT SQL statement for a log event with properly escaped values.
    /// PERF: Uses cached _insertPrefix to avoid repeated string interpolation of the table name.
    /// </summary>
    private string BuildInsertSql(LogEvent logEvent)
    {
        var timestamp = logEvent.Timestamp.UtcDateTime.ToString("o");
        var level = logEvent.Level.ToString();
        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception?.ToString();
        var properties = SerializeProperties(logEvent.Properties);

        var sb = new StringBuilder(_insertPrefix.Length + 256);
        sb.Append(_insertPrefix);
        sb.Append('\'').Append(EscapeSqlValue(timestamp)).Append("', ");
        sb.Append('\'').Append(EscapeSqlValue(level)).Append("', ");
        sb.Append('\'').Append(EscapeSqlValue(message)).Append("', ");

        if (exception is null)
        {
            sb.Append("NULL, ");
        }
        else
        {
            sb.Append('\'').Append(EscapeSqlValue(exception)).Append("', ");
        }

        if (properties is null)
        {
            sb.Append("NULL)");
        }
        else
        {
            sb.Append('\'').Append(EscapeSqlValue(properties)).Append("')");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes single quotes in SQL string values to prevent injection.
    /// </summary>
    private static string EscapeSqlValue(string value) => value.Replace("'", "''");

    /// <summary>
    /// Serializes log event properties to a JSON-like string.
    /// PERF: Pre-allocates StringBuilder with estimated capacity to reduce resizing.
    /// </summary>
    private static string? SerializeProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        // PERF: Estimate capacity based on property count to reduce StringBuilder resizing
        var sb = new StringBuilder(properties.Count * 64);
        using var writer = new StringWriter(sb);

        writer.Write('{');
        var first = true;

        foreach (var kvp in properties)
        {
            if (!first)
            {
                writer.Write(',');
            }
            first = false;

            writer.Write('"');
            writer.Write(kvp.Key);
            writer.Write("\":");

            kvp.Value.Render(writer);
        }

        writer.Write('}');
        writer.Flush();

        return sb.ToString();
    }

    /// <summary>
    /// Ensures the log table exists. Thread-safe via C# 14 Lock class with double-check pattern.
    /// </summary>
    private void EnsureTableCreated()
    {
        if (_tableCreated)
        {
            return;
        }

        lock (_initLock)
        {
            // Double-check after acquiring lock
            if (_tableCreated)
            {
                return;
            }

            try
            {
                // Create table with ULID for better performance and distributed compatibility
                // ULID is sortable (contains timestamp) and more efficient than GUID
                var createTableSql = $"""
                    CREATE TABLE {_tableName} (
                        Id ULID AUTO PRIMARY KEY,
                        Timestamp DATETIME,
                        Level TEXT,
                        Message TEXT,
                        Exception TEXT,
                        Properties TEXT
                    ) ENGINE={_storageEngine}
                    """;

                _database.ExecuteSQL(createTableSql);
                _tableCreated = true;
            }
            catch (Exception ex)
            {
                // Table might already exist — verify with a simple query
                try
                {
                    _database.ExecuteQuery($"SELECT COUNT(*) FROM {_tableName}");
                    _tableCreated = true;
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to verify table existence: {innerEx}");
                    throw new InvalidOperationException(
                        $"Could not create or verify log table '{_tableName}'.", ex);
                }
            }
        }
    }

    /// <summary>
    /// Disposes the sink and releases resources.
    /// Sealed class — simplified Dispose without Dispose(bool) pattern.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
