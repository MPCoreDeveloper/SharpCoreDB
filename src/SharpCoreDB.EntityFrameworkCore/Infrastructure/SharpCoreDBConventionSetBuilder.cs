using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Convention set builder for SharpCoreDB.
/// Ensures relational conventions (including RelationalModelConvention for table-entity mappings)
/// are properly added to the model building pipeline.
/// </summary>
public class SharpCoreDBConventionSetBuilder(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies)
    : RelationalConventionSetBuilder(dependencies, relationalDependencies)
{
}
