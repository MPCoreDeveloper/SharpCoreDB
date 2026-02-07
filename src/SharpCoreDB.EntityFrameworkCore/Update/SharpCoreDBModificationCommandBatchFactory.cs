using Microsoft.EntityFrameworkCore.Update;

namespace SharpCoreDB.EntityFrameworkCore.Update;

/// <summary>
/// Modification command batch factory for SharpCoreDB.
/// Creates batches for INSERT/UPDATE/DELETE operations using singular (one-command-per-batch) strategy.
/// </summary>
public class SharpCoreDBModificationCommandBatchFactory(
    ModificationCommandBatchFactoryDependencies dependencies) : IModificationCommandBatchFactory
{
    private readonly ModificationCommandBatchFactoryDependencies _dependencies = dependencies
        ?? throw new ArgumentNullException(nameof(dependencies));

    /// <inheritdoc />
    public ModificationCommandBatch Create()
    {
        return new SingularModificationCommandBatch(_dependencies);
    }
}
