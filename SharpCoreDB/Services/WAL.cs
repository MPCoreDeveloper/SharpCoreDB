using System.IO;
using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of IWAL using a log file with buffered I/O for improved performance.
/// </summary>
public class WAL : IWAL, IDisposable
{
    private readonly string _logPath;
    private readonly FileStream _fileStream;
    private readonly StreamWriter _writer;
    private bool _disposed = false;
    private int _logCount = 0;
    private const int FlushThreshold = 100; // Flush every 100 operations

    /// <summary>
    /// Initializes a new instance of the WAL class.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="config">Optional database configuration for buffer size.</param>
    public WAL(string dbPath, DatabaseConfig? config = null)
    {
        _logPath = Path.Combine(dbPath, PersistenceConstants.WalFileName);
        var bufferSize = config?.WalBufferSize ?? 1024 * 1024; // Default 1MB buffer
        
        // Use FileStream with buffering for better performance
        _fileStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.Asynchronous);
        _writer = new StreamWriter(_fileStream);
    }

    /// <inheritdoc />
    public void Log(string operation)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WAL));
        _writer.WriteLine(operation);
        _logCount++;
        
        // Batch writes - only flush periodically instead of every write
        if (_logCount >= FlushThreshold)
        {
            _writer.Flush();
            _logCount = 0;
        }
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WAL));
        
        // Ensure all buffered data is written before committing
        _writer.Flush();
        _fileStream.Flush(flushToDisk: true);
        _writer.Close();
        _fileStream.Close();
        
        File.Delete(_logPath);
        _disposed = true;
    }

    /// <summary>
    /// Asynchronously flushes buffered data to disk.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WAL));
        await _writer.FlushAsync();
        await _fileStream.FlushAsync();
        _logCount = 0;
    }

    /// <summary>
    /// Disposes the WAL instance and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _fileStream?.Dispose();
            _disposed = true;
        }
    }
}
