using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides a DbTransaction implementation for SharpCoreDB.
/// Note: This is a simplified implementation as SharpCoreDB transactions are currently limited.
/// </summary>
internal class DapperTransaction : DbTransaction
{
    private readonly DapperConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _isCommitted;
    private bool _isRolledBack;
    private readonly List<string> _sqlStatements = [];

    public DapperTransaction(DapperConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _isolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel => _isolationLevel;

    protected override DbConnection? DbConnection => _connection;

    /// <summary>
    /// Tracks SQL statement for transaction.
    /// </summary>
    internal void TrackStatement(string sql)
    {
        if (_isCommitted || _isRolledBack)
            throw new InvalidOperationException("Transaction has already been completed");
        
        _sqlStatements.Add(sql);
    }

    public override void Commit()
    {
        if (_isCommitted)
            throw new InvalidOperationException("Transaction has already been committed");
        
        if (_isRolledBack)
            throw new InvalidOperationException("Transaction has already been rolled back");

        try
        {
            // In a full implementation, this would commit all tracked statements
            // For now, statements are executed immediately in SharpCoreDB
            _isCommitted = true;
        }
        catch
        {
            _isRolledBack = true;
            throw;
        }
    }

    public override void Rollback()
    {
        if (_isCommitted)
            throw new InvalidOperationException("Transaction has already been committed");
        
        if (_isRolledBack)
            throw new InvalidOperationException("Transaction has already been rolled back");

        try
        {
            // In a full implementation, this would rollback all tracked statements
            // SharpCoreDB would need WAL-based rollback support
            _isRolledBack = true;
        }
        catch
        {
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_isCommitted && !_isRolledBack)
            {
                try
                {
                    Rollback();
                }
                catch
                {
                    // Suppress exceptions during disposal
                }
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Enhanced transaction support with savepoints (for future implementation).
/// </summary>
public class SharpCoreTransaction : IDisposable
{
    private readonly DapperConnection _connection;
    private DapperTransaction? _transaction;
    private readonly Stack<string> _savepoints = new();
    private bool _disposed;

    internal SharpCoreTransaction(DapperConnection connection, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        // Transaction creation is deferred until needed
    }

    /// <summary>
    /// Creates a savepoint in the transaction.
    /// </summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    public void CreateSavepoint(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharpCoreTransaction));

        _savepoints.Push(savepointName);
    }

    /// <summary>
    /// Rolls back to a specific savepoint.
    /// </summary>
    /// <param name="savepointName">Name of the savepoint.</param>
    public void RollbackToSavepoint(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);
        
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharpCoreTransaction));

        while (_savepoints.Count > 0)
        {
            var sp = _savepoints.Pop();
            if (sp == savepointName)
                break;
        }
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    public void Commit()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharpCoreTransaction));

        _transaction?.Commit();
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    public void Rollback()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SharpCoreTransaction));

        _transaction?.Rollback();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _transaction?.Dispose();
            _savepoints.Clear();
        }

        _disposed = true;
    }
}
