using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a transaction for SharpCoreDB that wraps EF Core transaction semantics.
/// </summary>
public class SharpCoreDBTransaction : IDbContextTransaction
{
    private readonly IRelationalConnection _connection;
    private readonly DbTransaction _dbTransaction;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Database.Connection> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBTransaction class.
    /// </summary>
    public SharpCoreDBTransaction(
        IRelationalConnection connection,
        DbTransaction dbTransaction,
        IDiagnosticsLogger<DbLoggerCategory.Database.Connection> logger,
        Guid? transactionId = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _dbTransaction = dbTransaction ?? throw new ArgumentNullException(nameof(dbTransaction));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TransactionId = transactionId ?? Guid.NewGuid();
    }

    /// <inheritdoc />
    public Guid TransactionId { get; }

    /// <inheritdoc />
    public void Commit()
    {
        _dbTransaction.Commit();
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _dbTransaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Rollback()
    {
        _dbTransaction.Rollback();
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _dbTransaction.RollbackAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _dbTransaction.Dispose();
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _dbTransaction.DisposeAsync().ConfigureAwait(false);
            _disposed = true;
        }
    }
}
