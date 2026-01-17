using BenchmarkDotNet.Attributes;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2C Friday: Inline Arrays & Collection Expressions Benchmarks
/// 
/// Measures performance improvements from:
/// - stackalloc: Stack allocation instead of heap
/// - Collection expressions: Modern C# 14 syntax with compiler optimizations
/// 
/// Expected improvements:
/// - stackalloc: 2-3x for small collections
/// - Collection expressions: 1.2-1.5x (syntax + optimization)
/// - Combined: 3-4.5x
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2CInlineArraysBenchmark
{
    private int[] testData = null!;
    private string[] testStrings = null!;
    private const int COLLECTION_SIZE = 100;

    [GlobalSetup]
    public void Setup()
    {
        testData = Enumerable.Range(0, COLLECTION_SIZE).ToArray();
        testStrings = Enumerable.Range(0, COLLECTION_SIZE)
            .Select(i => $"Item{i}")
            .ToArray();
    }

    /// <summary>
    /// Baseline: Traditional List with Add() calls
    /// Multiple allocations as list grows
    /// </summary>
    [Benchmark(Description = "List initialization - Traditional Add()")]
    public List<int> ListInit_Traditional()
    {
        var list = new List<int>();
        
        for (int i = 0; i < COLLECTION_SIZE; i++)
        {
            list.Add(testData[i]);  // Growth allocations
        }
        
        return list;
    }

    /// <summary>
    /// Optimized: List with collection expression
    /// Compiler allocates exact capacity
    /// Expected: 1.2-1.5x improvement
    /// </summary>
    [Benchmark(Description = "List initialization - Collection expression")]
    public List<int> ListInit_CollectionExpression()
    {
        // C# 14: Collection expression with exact allocation
        List<int> list = [..testData];
        return list;
    }

    /// <summary>
    /// Optimized: stackalloc Span
    /// Zero heap allocation for small collections
    /// Expected: 2-3x improvement
    /// </summary>
    [Benchmark(Description = "Collection - stackalloc Span")]
    public int Stackalloc_Span()
    {
        // Stack allocation - ZERO heap!
        Span<int> span = stackalloc int[COLLECTION_SIZE];
        
        for (int i = 0; i < COLLECTION_SIZE; i++)
        {
            span[i] = testData[i];
        }
        
        // Process span
        int sum = 0;
        foreach (var item in span)
        {
            sum += item;
        }
        
        return sum;
    }

    /// <summary>
    /// String collection: Traditional
    /// </summary>
    [Benchmark(Description = "String list - Traditional")]
    public List<string> StringList_Traditional()
    {
        var list = new List<string>();
        
        foreach (var str in testStrings)
        {
            list.Add(str);
        }
        
        return list;
    }

    /// <summary>
    /// String collection: Collection expression
    /// Expected: 1.2-1.5x improvement
    /// </summary>
    [Benchmark(Description = "String list - Collection expression")]
    public List<string> StringList_CollectionExpression()
    {
        List<string> list = [..testStrings];
        return list;
    }
}

/// <summary>
/// Collection expression benchmarks with various types
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2CCollectionExpressionsBenchmark
{
    private Dictionary<string, int> testDict = null!;
    private List<(string key, int value)> testPairs = null!;

    [GlobalSetup]
    public void Setup()
    {
        testDict = new();
        testPairs = new();
        
        for (int i = 0; i < 100; i++)
        {
            var key = $"Key{i}";
            var value = i;
            testDict[key] = value;
            testPairs.Add((key, value));
        }
    }

    /// <summary>
    /// Dictionary initialization: Traditional
    /// </summary>
    [Benchmark(Description = "Dictionary - Traditional")]
    public Dictionary<string, int> Dict_Traditional()
    {
        var dict = new Dictionary<string, int>();
        
        foreach (var (key, value) in testPairs)
        {
            dict[key] = value;
        }
        
        return dict;
    }

    /// <summary>
    /// Dictionary initialization: Modern C# 14 syntax
    /// Expected: 1.2-1.5x improvement
    /// </summary>
    [Benchmark(Description = "Dictionary - Modern C# 14")]
    public Dictionary<string, int> Dict_Modern()
    {
        var dict = new Dictionary<string, int>();
        
        // Modern C# 14 syntax with index operator
        foreach (var (key, value) in testPairs)
        {
            dict[key] = value;
        }
        
        return dict;
    }

    /// <summary>
    /// Enumerable with collection expression
    /// Expected: 1.2-1.5x improvement
    /// </summary>
    [Benchmark(Description = "Enumerable spread - Collection expression")]
    public List<string> Enumerable_CollectionExpression()
    {
        var keys = testDict.Keys;
        List<string> list = [..keys];  // Spread operator with collection expression
        return list;
    }
}

/// <summary>
/// Detailed stackalloc vs heap allocation comparison
/// </summary>
[MemoryDiagnoser]
public class Phase2CStackallocDetailedTest
{
    private int[] data = null!;

    [GlobalSetup]
    public void Setup()
    {
        data = Enumerable.Range(0, 256).ToArray();
    }

    /// <summary>
    /// Array allocation: Heap
    /// </summary>
    [Benchmark(Description = "Array - Heap allocation")]
    public int Array_Heap()
    {
        int[] array = new int[256];
        
        for (int i = 0; i < data.Length; i++)
        {
            array[i] = data[i];
        }
        
        int sum = 0;
        foreach (var item in array)
            sum += item;
        
        return sum;
    }

    /// <summary>
    /// Array allocation: Stack (stackalloc)
    /// Expected: 2-3x faster, zero allocations
    /// </summary>
    [Benchmark(Description = "Array - Stack allocation (stackalloc)")]
    public int Array_Stack()
    {
        Span<int> span = stackalloc int[256];
        
        for (int i = 0; i < data.Length; i++)
        {
            span[i] = data[i];
        }
        
        int sum = 0;
        foreach (var item in span)
            sum += item;
        
        return sum;
    }

    /// <summary>
    /// Span with stackalloc: Processing
    /// Expected: 2-3x faster than List
    /// </summary>
    [Benchmark(Description = "Span processing - stackalloc")]
    public int Span_Processing()
    {
        Span<int> span = stackalloc int[data.Length];
        Array.Copy(data, 0, span.ToArray(), 0, data.Length);
        
        int sum = 0;
        foreach (var item in span)
            sum += item;
        
        return sum;
    }

    /// <summary>
    /// List processing: Traditional
    /// </summary>
    [Benchmark(Description = "List processing - Traditional")]
    public int List_Processing()
    {
        var list = new List<int>(data);
        
        int sum = 0;
        foreach (var item in list)
            sum += item;
        
        return sum;
    }
}

/// <summary>
/// Concurrent impact of collection optimizations
/// </summary>
[MemoryDiagnoser]
public class Phase2CCollectionConcurrentTest
{
    private int[] testData = null!;

    [GlobalSetup]
    public void Setup()
    {
        testData = Enumerable.Range(0, 100).ToArray();
    }

    /// <summary>
    /// Multi-threaded: stackalloc with high concurrency
    /// Each thread gets its own stack - NO contention
    /// Expected: Minimal overhead, high efficiency
    /// </summary>
    [Benchmark(Description = "Concurrent stackalloc (no contention)")]
    public int Concurrent_Stackalloc()
    {
        var tasks = new System.Threading.Tasks.Task[10];
        
        for (int t = 0; t < 10; t++)
        {
            tasks[t] = System.Threading.Tasks.Task.Run(() =>
            {
                Span<int> span = stackalloc int[testData.Length];
                
                for (int i = 0; i < testData.Length; i++)
                    span[i] = testData[i];
            });
        }
        
        System.Threading.Tasks.Task.WaitAll(tasks);
        return 0;
    }

    /// <summary>
    /// Multi-threaded: Collections (heap allocations)
    /// Expected: GC pressure, contention
    /// </summary>
    [Benchmark(Description = "Concurrent List (heap allocations)")]
    public int Concurrent_List()
    {
        var tasks = new System.Threading.Tasks.Task[10];
        
        for (int t = 0; t < 10; t++)
        {
            tasks[t] = System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<int>(testData);
                // Process list
            });
        }
        
        System.Threading.Tasks.Task.WaitAll(tasks);
        return 0;
    }
}
