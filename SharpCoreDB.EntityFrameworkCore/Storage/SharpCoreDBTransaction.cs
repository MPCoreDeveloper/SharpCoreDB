using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a transaction for SharpCoreDB.
/// </summary>
public class SharpCoreDBTransaction : DbTransaction
{
    private readonly SharpCoreDBConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBTransaction class.
    /// </summary>
    public SharpCoreDBTransaction(SharpCoreDBConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _isolationLevel = isolationLevel;
    }

    /// <inheritdoc />
    protected override DbConnection? DbConnection => _connection;

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _isolationLevel;

    /// <inheritdoc />
    public override void Commit()
    {
        // SharpCoreDB uses WAL for transactions
        // This is a simplified implementation
    }

    /// <inheritdoc />
    public override void Rollback()
    {
        // SharpCoreDB WAL rollback
        // Simplified implementation
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
