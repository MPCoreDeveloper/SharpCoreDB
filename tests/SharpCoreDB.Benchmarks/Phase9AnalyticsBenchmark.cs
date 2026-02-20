using BenchmarkDotNet.Attributes;
using SharpCoreDB.Analytics.OLAP;
using SharpCoreDB.Analytics.TimeSeries;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 9.7: Analytics performance benchmarks for time-series and OLAP pivoting.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase9AnalyticsBenchmark
{
    private double[] _series = [];
    private List<Sale> _sales = [];

    [GlobalSetup]
    public void Setup()
    {
        _series = Enumerable.Range(1, 100_000).Select(static value => (double)value).ToArray();
        _sales =
        [
            new Sale("North", "Electronics", 500m),
            new Sale("North", "Food", 200m),
            new Sale("South", "Electronics", 300m),
            new Sale("South", "Food", 150m),
            new Sale("East", "Electronics", 400m),
            new Sale("East", "Food", 250m)
        ];
    }

    [Benchmark(Description = "Rolling SUM (window=30)")]
    public double RollingSum_Window30()
    {
        double? last = null;
        foreach (var value in _series.RollingSum(static v => v, 30))
        {
            last = value;
        }

        return last ?? 0d;
    }

    [Benchmark(Description = "OLAP Pivot Table Build")]
    public int PivotTable_Build()
    {
        var pivot = _sales
            .AsOlapCube()
            .WithDimensions(sale => sale.Region, sale => sale.Product)
            .WithMeasure(group => group.Sum(sale => sale.Amount))
            .ToPivotTable();

        return pivot.RowHeaders.Count;
    }

    private sealed record Sale(string Region, string Product, decimal Amount);
}
