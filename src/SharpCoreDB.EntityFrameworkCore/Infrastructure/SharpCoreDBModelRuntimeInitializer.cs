using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Model runtime initializer for SharpCoreDB.
/// Forces eager relational model creation during initialization to ensure
/// column-property mappings reference the RuntimeEntityType instances
/// from the RuntimeModel (not the design-time EntityType instances).
/// Without this, lazy creation via GetRelationalModel() may use the
/// design-time model, causing entity type reference mismatches in queries.
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
        // Pass designTime=false to the base to prevent it from creating the relational
        // model using the design-time model's entity types (which causes reference mismatches
        // with the RuntimeModel's RuntimeEntityType instances during query compilation).
        model = base.Initialize(model, designTime: false, validationLogger);

        // Now force relational model creation on the RuntimeModel.
        // GetRelationalModel() will use GetOrAddRuntimeAnnotationValue to lazily create
        // the relational model from the RuntimeModel's entity types.
        model.GetRelationalModel();

        return model;
    }
}
