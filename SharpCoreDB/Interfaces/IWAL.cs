namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for Write-Ahead Logging to ensure ACID properties.
/// </summary>
public interface IWAL
{
    /// <summary>
    /// Logs an operation.
    /// </summary>
    /// <param name="operation">The operation to log.</param>
    void Log(string operation);

    /// <summary>
    /// Commits the log, clearing it after successful write.
    /// </summary>
    void Commit();

    /// <summary>
    /// Asynchronously flushes buffered data to disk.
    /// </summary>
    Task FlushAsync();
}
