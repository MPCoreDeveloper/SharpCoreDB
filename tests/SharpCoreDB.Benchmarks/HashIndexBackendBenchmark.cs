namespace SharpCoreDB.Benchmarks;

using BenchmarkDotNet.Attributes;
using SharpCoreDB.DataStructures;

/// <summary>
/// Benchmarks classic <see cref="HashIndex"/> backend versus the unsafe equality backend.
/// </summary>
[MemoryDiagnoser]
public class HashIndexBackendBenchmark
{
    private readonly List<Dictionary<string, object>> _rows = [];
    private HashIndex _index = null!;

    [Params(false, true)]
    public bool UseUnsafeEqualityIndex { get; set; }

    [Params(10_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _rows.Clear();
        for (int i = 0; i < RowCount; i++)
        {
            _rows.Add(new Dictionary<string, object>
            {
                ["project"] = $"project_{i % 100}",
                ["duration"] = i,
            });
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _index?.Dispose();
        _index = new HashIndex(
            "bench",
            "project",
            CollationType.Binary,
            isUnique: false,
            useUnsafeEqualityIndex: UseUnsafeEqualityIndex);

        for (int i = 0; i < _rows.Count; i++)
        {
            _index.Add(_rows[i], i);
        }
    }

    [Benchmark]
    public List<long> LookupHotKey()
    {
        return _index.LookupPositions("project_42");
    }

    [Benchmark]
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey) GetStatistics()
    {
        return _index.GetStatistics();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _index?.Dispose();
    }
}
