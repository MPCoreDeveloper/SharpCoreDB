using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents an ADO.NET transaction for SharpCoreDB.
/// This is the low-level <see cref="DbTransaction"/> used by <see cref="SharpCoreDBConnection"/>.
/// Wires Commit/Rollback to IDatabase.EndBatchUpdate/CancelBatchUpdate for real transaction semantics.
/// </summary>
public class SharpCoreDBDbTransaction : DbTransaction
{
    private readonly SharpCoreDBConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBDbTransaction"/> class.
    /// Begins a batch update transaction on the underlying database if available.
    /// </summary>
    public SharpCoreDBDbTransaction(SharpCoreDBConnection connection, IsolationLevel isolationLevel)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _isolationLevel = isolationLevel;

        // Use IDatabase.BeginBatchUpdate() which works for both Database and SingleFileDatabase
        var db = _connection.DbInstance;
        if (db is not null && !db.IsBatchUpdateActive)
        {
            db.BeginBatchUpdate();
        }
    }

    /// <inheritdoc />
    protected override DbConnection? DbConnection => _connection;

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _isolationLevel;

    /// <inheritdoc />
    public override void Commit()
    {
        if (_completed)
            return;

        _completed = true;

        var db = _connection.DbInstance;
        if (db is not null && db.IsBatchUpdateActive)
        {
            // Use IDatabase.EndBatchUpdate() which works for both Database and SingleFileDatabase
            db.EndBatchUpdate();
            db.Flush();
        }
    }

    /// <inheritdoc />
    public override void Rollback()
    {
        if (_completed)
            return;

        _completed = true;

        var db = _connection.DbInstance;
        if (db is not null && db.IsBatchUpdateActive)
        {
            // Use IDatabase.CancelBatchUpdate() which works for both Database and SingleFileDatabase
            db.CancelBatchUpdate();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Auto-rollback uncommitted transaction
            if (!_completed)
            {
                Rollback();
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
