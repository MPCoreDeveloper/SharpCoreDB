using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SharpCoreDB.EntityFrameworkCore.Query;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Customizes the EF Core model for SharpCoreDB.
/// </summary>
public sealed class SharpCoreDBModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder
            .HasDbFunction(typeof(SharpCoreDBDbFunctionsExtensions)
                .GetMethod(nameof(SharpCoreDBDbFunctionsExtensions.GraphTraverse))!)
            .HasName("GRAPH_TRAVERSE");
    }
}
