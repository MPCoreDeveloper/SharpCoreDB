using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Convention set builder for SharpCoreDB.
/// Ensures relational conventions are applied and that integer primary keys
/// receive the ValueGeneratedOnAdd annotation by default (matching SQLite provider behavior).
/// </summary>
public class SharpCoreDBConventionSetBuilder(
    ProviderConventionSetBuilderDependencies dependencies,
    RelationalConventionSetBuilderDependencies relationalDependencies)
    : RelationalConventionSetBuilder(dependencies, relationalDependencies)
{
}
