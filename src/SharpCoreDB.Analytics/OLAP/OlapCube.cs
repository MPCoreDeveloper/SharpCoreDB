namespace SharpCoreDB.Analytics.OLAP;

using System.Globalization;

/// <summary>
/// Provides OLAP-style cube construction for pivoting.
/// </summary>
public sealed class OlapCube<T>(IEnumerable<T> source)
{
    private readonly IEnumerable<T> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly List<Func<T, object?>> _dimensions = [];
    private Func<IEnumerable<T>, object?>? _measure;

    /// <summary>
    /// Configures the cube dimensions.
    /// </summary>
    /// <param name="dimensions">Dimension selectors.</param>
    /// <returns>The configured cube.</returns>
    /// <exception cref="ArgumentException">Thrown when no dimensions are provided.</exception>
    public OlapCube<T> WithDimensions(params Func<T, object?>[] dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Length == 0)
        {
            throw new ArgumentException("At least one dimension is required.", nameof(dimensions));
        }

        _dimensions.Clear();
        _dimensions.AddRange(dimensions);
        return this;
    }

    /// <summary>
    /// Configures the cube measure.
    /// </summary>
    /// <param name="measure">Measure aggregation function.</param>
    /// <returns>The configured cube.</returns>
    public OlapCube<T> WithMeasure(Func<IEnumerable<T>, object?> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        _measure = measure;
        return this;
    }

    /// <summary>
    /// Builds a pivot table for a two-dimension cube.
    /// </summary>
    /// <returns>The resulting pivot table.</returns>
    /// <exception cref="InvalidOperationException">Thrown when dimensions or measures are not configured.</exception>
    public PivotTable ToPivotTable()
    {
        if (_dimensions.Count != 2)
        {
            throw new InvalidOperationException("Pivot tables require exactly two dimensions.");
        }

        if (_measure is null)
        {
            throw new InvalidOperationException("Pivot tables require a measure to be configured.");
        }

        var groups = _source.GroupBy(item => (
            Row: NormalizeKey(_dimensions[0](item)),
            Column: NormalizeKey(_dimensions[1](item))));

        var rowHeaders = groups.Select(group => group.Key.Row).Distinct().OrderBy(static key => key).ToList();
        var columnHeaders = groups.Select(group => group.Key.Column).Distinct().OrderBy(static key => key).ToList();

        Dictionary<(string Row, string Column), object?> values = [];
        foreach (var group in groups)
        {
            values[(group.Key.Row, group.Key.Column)] = _measure(group);
        }

        return new PivotTable(rowHeaders, columnHeaders, values);
    }

    private static string NormalizeKey(object? value)
    {
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
    }
}
