using Microsoft.EntityFrameworkCore.Update;

namespace SharpCoreDB.EntityFrameworkCore.Update;

/// <summary>
/// Modification command batch factory for SharpCoreDB.
/// Creates batches for INSERT/UPDATE/DELETE operations.
/// INCOMPLETE IMPLEMENTATION - Placeholder stub only.
/// </summary>
public class SharpCoreDBModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <inheritdoc />
    public ModificationCommandBatch Create()
    {
        throw new NotImplementedException("EF Core provider is incomplete - see EFCORE_STATUS.md");
    }
}
