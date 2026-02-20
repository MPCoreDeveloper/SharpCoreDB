namespace SharpCoreDB.Analytics.OLAP;

/// <summary>
/// Extension methods for OLAP analytics.
/// </summary>
public static class OlapExtensions
{
    /// <summary>
    /// Creates a new OLAP cube from a sequence.
    /// </summary>
    public static OlapCube<T> AsOlapCube<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new OlapCube<T>(source);
    }
}
