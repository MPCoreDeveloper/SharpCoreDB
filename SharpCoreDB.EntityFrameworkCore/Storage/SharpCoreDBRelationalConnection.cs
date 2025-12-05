using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Relational connection implementation for SharpCoreDB.
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBRelationalConnection : IRelationalConnection
{
    /// <inheritdoc />
    public string? ConnectionString => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");

    /// <inheritdoc />
    public DbConnection? DbConnection => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");

    /// <inheritdoc />
    public Guid ConnectionId => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");

    /// <inheritdoc />
    public int? CommandTimeout
    {
        get => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
        set => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public IDbContextTransaction? CurrentTransaction => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");

    /// <inheritdoc />
    public SemaphoreSlim Semaphore => throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");

    /// <inheritdoc />
    public IDbContextTransaction BeginTransaction()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public void CommitTransaction()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public void RollbackTransaction()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public IDbContextTransaction UseTransaction(DbTransaction? transaction)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<IDbContextTransaction> UseTransactionAsync(DbTransaction? transaction, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public bool Open(bool errorsExpected = false)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public bool Close()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<bool> CloseAsync()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No-op for stub
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
