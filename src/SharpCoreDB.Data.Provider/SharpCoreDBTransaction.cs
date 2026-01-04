using System.Data;
using System.Data.Common;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Represents a transaction to be performed at a SharpCoreDB database.
/// Uses SharpCoreDB's batch update mechanism for transaction support.
/// Modern C# 14 implementation.
/// </summary>
public sealed class SharpCoreDBTransaction : DbTransaction
{
    private readonly SharpCoreDBConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _isCompleted;

    internal SharpCoreDBTransaction(SharpCoreDBConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _isolationLevel = isolationLevel;
        _isCompleted = false;

        // Begin batch update for transaction
        _connection.DbInstance?.BeginBatchUpdate();
    }

    /// <summary>
    /// Gets the connection associated with the transaction.
    /// </summary>
    protected override DbConnection? DbConnection => _connection;

    /// <summary>
    /// Gets the isolation level for the transaction.
    /// </summary>
    public override IsolationLevel IsolationLevel => _isolationLevel;

    /// <summary>
    /// Commits the database transaction.
    /// </summary>
    public override void Commit()
    {
        if (_isCompleted)
            throw new InvalidOperationException("Transaction has already been completed.");

        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to commit transaction.");

        try
        {
            _connection.DbInstance?.EndBatchUpdate();
            _isCompleted = true;
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Failed to commit transaction.", ex);
        }
    }

    /// <summary>
    /// Rolls back the transaction.
    /// </summary>
    public override void Rollback()
    {
        if (_isCompleted)
            throw new InvalidOperationException("Transaction has already been completed.");

        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to rollback transaction.");

        try
        {
            _connection.DbInstance?.CancelBatchUpdate();
            _isCompleted = true;
        }
        catch (Exception ex)
        {
            throw new SharpCoreDBException("Failed to rollback transaction.", ex);
        }
    }

    /// <summary>
    /// Disposes the transaction.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isCompleted)
        {
            try
            {
                Rollback();
            }
            catch
            {
                // Ignore rollback errors during dispose
            }
        }
        base.Dispose(disposing);
    }
}
