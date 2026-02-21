using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Model runtime initializer for SharpCoreDB.
/// Ensures the relational model is properly created from RuntimeEntityType instances
/// so that table mappings reference the correct entity type objects during query compilation.
/// </summary>
public class SharpCoreDBModelRuntimeInitializer(
    ModelRuntimeInitializerDependencies dependencies,
    RelationalModelRuntimeInitializerDependencies relationalDependencies)
    : RelationalModelRuntimeInitializer(dependencies, relationalDependencies)
{
    /// <inheritdoc />
    public override IModel Initialize(
        IModel model,
        bool designTime = true,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation>? validationLogger = null)
    {
        // Let the base handle model finalization and relational model creation.
        // Pass designTime=true so that RelationalModelRuntimeInitializer eagerly
        // creates the relational model with correct entity type references.
        return base.Initialize(model, designTime: true, validationLogger);
    }
}
