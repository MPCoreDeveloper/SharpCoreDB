namespace SharpCoreDB.Functional.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Entry-point extensions for EF Core functional adapters.
/// </summary>
public static class FunctionalEntityFrameworkCoreExtensions
{
    /// <summary>
    /// Creates a functional wrapper around a <see cref="DbContext"/> instance.
    /// </summary>
    /// <param name="context">The source DbContext.</param>
    /// <returns>The functional EF wrapper.</returns>
    public static FunctionalEfDb Functional(this DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new FunctionalEfDb(context);
    }
}
