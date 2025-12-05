using Microsoft.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database creator implementation for SharpCoreDB.
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBDatabaseCreator : IDatabaseCreator
{
    /// <inheritdoc />
    public bool EnsureCreated()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public bool EnsureDeleted()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public bool CanConnect()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }

    /// <inheritdoc />
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }
}
