using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a ADO.NET transaction for SharpCoreDB.
/// This is the low-level DbTransaction used by SharpCoreDBConnection.
/// </summary>
public class SharpCoreDBDbTransaction : DbTransaction
{
    private readonly SharpCoreDBConnection _connection;
    private readonly IsolationLevel _isolationLevel;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDbTransaction class.
    /// </summary>
    public SharpCoreDBDbTransaction(SharpCoreDBConnection connection, IsolationLevel isolationLevel)
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
