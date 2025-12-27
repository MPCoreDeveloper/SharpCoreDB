using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using SharpCoreDB.Interfaces;
using System.Text;

namespace SharpCoreDB.Serilog.Sinks;

/// <summary>
/// Serilog sink that writes log events to a SharpCoreDB database.
/// Uses PeriodicBatchingSink for efficient batching of log writes.
/// </summary>
public class SharpCoreDBSink : IBatchedLogEventSink, IDisposable
{
    private readonly IDatabase _database;
    private readonly string _tableName;
    private readonly string _storageEngine;
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
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _storageEngine = storageEngine ?? "AppendOnly";

        if (autoCreateTable)
        {
            CreateTableIfNotExists();
        }
    }

    /// <summary>
    /// Emits a batch of log events to the database.
    /// </summary>
    /// <param name="batch">The batch of log events to write.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
    {
        if (_disposed)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(SharpCoreDBSink));
        }

        try
        {
            // Start batch update for better performance
            _database.BeginBatchUpdate();

            foreach (var logEvent in batch)
            {
                await WriteLogEventAsync(logEvent);
            }

            // Commit the batch
            _database.EndBatchUpdate();
        }
        catch (Exception)
        {
            // Cancel batch on error
            try
            {
                _database.CancelBatchUpdate();
            }
            catch (Exception cancelEx)
            {
                // Log or handle the cancellation error if needed
                // For now, just ignore errors during cancellation as originally intended
                System.Diagnostics.Debug.WriteLine($"Batch cancellation failed: {cancelEx}");
            }

            throw;
        }
    }

    /// <summary>
    /// Called when an empty batch is detected.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task OnEmptyBatchAsync()
    {
        return Task.CompletedTask;
    }

    private async Task WriteLogEventAsync(LogEvent logEvent)
    {
        var timestamp = logEvent.Timestamp.UtcDateTime;
        var level = logEvent.Level.ToString();
        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception?.ToString();
        var properties = SerializeProperties(logEvent.Properties);

        var sql = $"INSERT INTO {_tableName} (Timestamp, Level, Message, Exception, Properties) VALUES (@0, @1, @2, @3, @4)";
        var parameters = new Dictionary<string, object?>
        {
            { "0", timestamp },
            { "1", level },
            { "2", message },
            { "3", exception },
            { "4", properties }
        };

        await _database.ExecuteSQLAsync(sql, parameters);
    }

    private static  string? SerializeProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
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

    private void CreateTableIfNotExists()
    {
        if (_tableCreated)
        {
            return;
        }

        try
        {
            // Create table with ULID for better performance and distributed compatibility
            // ULID is sortable (contains timestamp) and more efficient than GUID
            var createTableSql = $@"CREATE TABLE {_tableName} (
                Id ULID AUTO PRIMARY KEY,
                Timestamp DATETIME,
                Level TEXT,
                Message TEXT,
                Exception TEXT,
                Properties TEXT
            ) ENGINE={_storageEngine}";

            _database.ExecuteSQL(createTableSql);
            _tableCreated = true;
        }
        catch (Exception ex)
        {
            // Table might already exist - try a simple query to verify
            try
            {
                _database.ExecuteQuery($"SELECT COUNT(*) FROM {_tableName}");
                _tableCreated = true;
            }
            catch (Exception innerEx)
            {
                // Log the exception before rethrowing to satisfy S2737
                System.Diagnostics.Debug.WriteLine($"Failed to verify table existence: {innerEx}");
                throw ex;
            }
        }
    }

    /// <summary>
    /// Finalizer to ensure resources are released.
    /// </summary>
    ~SharpCoreDBSink()
    {
        Dispose(false);
    }

    /// <summary>
    /// Disposes the sink and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected implementation of Dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose; false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        // If disposing == true, dispose managed resources here.
        // No managed or unmanaged resources to dispose in this implementation.

        _disposed = true;
    }
}
