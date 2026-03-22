namespace SharpCoreDB.Functional;

using SharpCoreDB.Interfaces;

/// <summary>
/// Entry points for the functional facade over SharpCoreDB.
/// </summary>
public static class FunctionalExtensions
{
    /// <summary>
    /// Creates a functional wrapper around a <see cref="Database"/> instance.
    /// </summary>
    /// <param name="db">The inner database instance.</param>
    /// <returns>A new <see cref="FunctionalDb"/> wrapper.</returns>
    public static FunctionalDb Functional(this Database db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new(db);
    }

    /// <summary>
    /// Creates a functional wrapper around an <see cref="IDatabase"/> instance.
    /// </summary>
    /// <param name="db">The inner database abstraction.</param>
    /// <returns>A new <see cref="FunctionalDb"/> wrapper.</returns>
    public static FunctionalDb Functional(this IDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        return new(db);
    }
}
