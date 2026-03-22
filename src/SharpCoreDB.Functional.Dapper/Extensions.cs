namespace SharpCoreDB.Functional.Dapper;

using SharpCoreDB.Interfaces;

/// <summary>
/// Entry-point extensions for Dapper functional adapters.
/// </summary>
public static class FunctionalDapperExtensions
{
    /// <summary>
    /// Creates a functional Dapper wrapper from an <see cref="IDatabase"/> instance.
    /// </summary>
    /// <param name="database">The source SharpCoreDB instance.</param>
    /// <returns>The functional Dapper wrapper.</returns>
    public static FunctionalDapperDb FunctionalDapper(this IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new FunctionalDapperDb(database);
    }
}
