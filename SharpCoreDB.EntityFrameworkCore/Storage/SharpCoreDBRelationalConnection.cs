using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Relational connection implementation for SharpCoreDB.
/// Manages database connections, transactions, and command execution.
/// </summary>
public class SharpCoreDBRelationalConnection : IRelationalConnection
{
    private DbConnection? _connection;
    private IDbContextTransaction? _currentTransaction;
    private readonly SemaphoreSlim _semaphore = new(1);
    private int? _commandTimeout;
    private readonly IDbContextOptions _options;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Database.Connection> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBRelationalConnection class.
    /// </summary>
    public SharpCoreDBRelationalConnection(
        IDbContextOptions options,
        IDiagnosticsLogger<DbLoggerCategory.Database.Connection> logger,
        IServiceProvider serviceProvider)
    {
        _options = options;
        _logger = logger;
        _serviceProvider = serviceProvider;
        ConnectionId = Guid.NewGuid();
    }

    /// <inheritdoc />
    public string? ConnectionString
    {
        get => _connection?.ConnectionString;
        set
        {
            if (_connection == null)
            {
                _connection = new SharpCoreDBConnection(_serviceProvider, value ?? string.Empty);
            }
            else
            {
                _connection.ConnectionString = value ?? string.Empty;
            }
        }
    }

    /// <inheritdoc />
    public DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value;
    }

    /// <inheritdoc />
    public Guid ConnectionId { get; }

    /// <inheritdoc />
    public int? CommandTimeout
    {
        get => _commandTimeout;
        set => _commandTimeout = value;
    }

    /// <inheritdoc />
    public IDbContextTransaction? CurrentTransaction => _currentTransaction;

    /// <inheritdoc />
    public SemaphoreSlim Semaphore => _semaphore;

    /// <inheritdoc />
    public DbContext? Context { get; set; }

    /// <inheritdoc />
    public void SetDbConnection(DbConnection? connection, bool contextOwnsConnection)
    {
        _connection = connection;
    }

    /// <inheritdoc />
    public IRelationalCommand RentCommand()
    {
        // Return a command from the pool
        throw new NotSupportedException("Command pooling not yet implemented");
    }

    /// <inheritdoc />
    public void ReturnCommand(IRelationalCommand command)
    {
        // Return command to pool
        // No-op for now
    }

    /// <inheritdoc />
    public IDbContextTransaction BeginTransaction()
    {
        return BeginTransaction(IsolationLevel.Unspecified);
    }

    /// <inheritdoc />
    public IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Connection not initialized");
        }

        Open();
        var dbTransaction = _connection.BeginTransaction(isolationLevel);
        _currentTransaction = new SharpCoreDBTransaction(this, dbTransaction, _logger);
        return _currentTransaction;
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return BeginTransactionAsync(IsolationLevel.Unspecified, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Connection not initialized");
        }

        await OpenAsync(cancellationToken).ConfigureAwait(false);
        var dbTransaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        _currentTransaction = new SharpCoreDBTransaction(this, dbTransaction, _logger);
        return _currentTransaction;
    }

    /// <inheritdoc />
    public void CommitTransaction()
    {
        _currentTransaction?.Commit();
        _currentTransaction = null;
    }

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _currentTransaction = null;
        }
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
        _currentTransaction?.Rollback();
        _currentTransaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _currentTransaction = null;
        }
    }

    /// <inheritdoc />
    public IDbContextTransaction UseTransaction(DbTransaction? transaction)
    {
        return UseTransaction(transaction, Guid.NewGuid());
    }

    /// <inheritdoc />
    public IDbContextTransaction UseTransaction(DbTransaction? transaction, Guid transactionId)
    {
        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        _currentTransaction = new SharpCoreDBTransaction(this, transaction, _logger, transactionId);
        return _currentTransaction;
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, CancellationToken cancellationToken = default)
    {
        return UseTransactionAsync(transaction, Guid.NewGuid(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction?> UseTransactionAsync(DbTransaction? transaction, Guid transactionId, CancellationToken cancellationToken = default)
    {
        if (transaction == null)
        {
            return Task.FromResult<IDbContextTransaction?>(null);
        }

        _currentTransaction = new SharpCoreDBTransaction(this, transaction, _logger, transactionId);
        return Task.FromResult<IDbContextTransaction?>(_currentTransaction);
    }

    /// <inheritdoc />
    public bool Open(bool errorsExpected = false)
    {
        if (_connection == null)
        {
            var extension = _options.FindExtension<SharpCoreDBOptionsExtension>();
            if (extension == null)
            {
                throw new InvalidOperationException("SharpCoreDB options not configured");
            }
            _connection = new SharpCoreDBConnection(_serviceProvider, extension.ConnectionString);
        }

        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        if (_connection == null)
        {
            var extension = _options.FindExtension<SharpCoreDBOptionsExtension>();
            if (extension == null)
            {
                throw new InvalidOperationException("SharpCoreDB options not configured");
            }
            _connection = new SharpCoreDBConnection(_serviceProvider, extension.ConnectionString);
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool Close()
    {
        if (_connection?.State == ConnectionState.Open)
        {
            _connection.Close();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> CloseAsync()
    {
        if (_connection?.State == ConnectionState.Open)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public void ResetState()
    {
        // Reset connection state without closing
        _currentTransaction = null;
    }

    /// <inheritdoc />
    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        ResetState();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _connection?.Dispose();
        _semaphore.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.DisposeAsync().ConfigureAwait(false);
        }
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        _semaphore.Dispose();
    }
}
