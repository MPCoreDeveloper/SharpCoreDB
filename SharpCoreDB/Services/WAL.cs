using System.IO;
using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of IWAL using a log file.
/// </summary>
public class WAL : IWAL, IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter _writer;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the WAL class.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    public WAL(string dbPath)
    {
        _logPath = Path.Combine(dbPath,  PersistenceConstants.WalFileName);
        _writer = new StreamWriter(_logPath, true);
    }

    /// <inheritdoc />
    public void Log(string operation)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WAL));
        _writer.WriteLine(operation);
        _writer.Flush();
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WAL));
        _writer.Close();
        File.Delete(_logPath);
        _disposed = true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer?.Close();
            _disposed = true;
        }
    }
}
