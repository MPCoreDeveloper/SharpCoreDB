using BenchmarkDotNet.Attributes;
using SharpCoreDB.Optimization;
using System;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2E Wednesday: Cache Optimization Benchmarks
/// 
/// Tests spatial and temporal locality improvements.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2E_CacheOptimizationBenchmark
{
    private int[] testData = null!;
    private const int DataSize = 10000000;

    [GlobalSetup]
    public void Setup()
    {
        testData = new int[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            testData[i] = random.Next(-1000, 1000);
        }
    }

    /// <summary>
    /// Baseline: Sequential access (good cache behavior)
    /// Modern CPUs can prefetch this automatically.
    /// </summary>
    [Benchmark(Description = "Sum - Sequential (Baseline)")]
    public long Sum_Sequential()
    {
        long sum = 0;
        for (int i = 0; i < testData.Length; i++)
        {
            sum += testData[i];
        }
        return sum;
    }

    /// <summary>
    /// Optimized: Block-based processing.
    /// Keeps data in L2 cache, improves temporal locality.
    /// Expected: 1.2-1.3x faster
    /// </summary>
    [Benchmark(Description = "Sum - Block Processing")]
    public long Sum_BlockProcessing()
    {
        return CacheOptimizer.ProcessInBlocks(testData);
    }

    /// <summary>
    /// Optimized: Cache-line aware processing.
    /// Explicitly processes one cache line at a time.
    /// Expected: 1.3-1.5x faster
    /// </summary>
    [Benchmark(Description = "Sum - Cache-Line Aware")]
    public long Sum_CacheLineAware()
    {
        return CacheOptimizer.ProcessCacheLineAware(testData);
    }

    /// <summary>
    /// Optimized: Sequential with prefetch hints.
    /// Helps compiler understand prefetch patterns.
    /// Expected: 1.2-1.3x faster
    /// </summary>
    [Benchmark(Description = "Sum - Sequential Optimal")]
    public long Sum_SequentialOptimal()
    {
        return CacheOptimizer.SequentialAccessOptimal(testData);
    }

    /// <summary>
    /// Stride-aware processing.
    /// Tests how cache handles different stride patterns.
    /// </summary>
    [Benchmark(Description = "Sum - Stride=1")]
    public long Sum_Stride1()
    {
        return CacheOptimizer.StrideAwareSum(testData, stride: 1);
    }

    /// <summary>
    /// Stride-aware with larger stride.
    /// </summary>
    [Benchmark(Description = "Sum - Stride=4")]
    public long Sum_Stride4()
    {
        return CacheOptimizer.StrideAwareSum(testData, stride: 4);
    }

    /// <summary>
    /// Temporal locality: Multiple passes over data.
    /// Data stays in cache on second pass = much faster!
    /// </summary>
    [Benchmark(Description = "Sum - Temporal Locality (2 Passes)")]
    public long Sum_TemporalLocality()
    {
        return CacheOptimizer.TemporalLocalityOptimal(testData, passes: 2);
    }
}

/// <summary>
/// Phase 2E Wednesday: Columnar Storage Benchmarks
/// 
/// Tests cache efficiency of different data layouts.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_ColumnarStorageBenchmark
{
    private int[] col1 = null!;
    private int[] col2 = null!;
    private int[] col3 = null!;
    private const int RowCount = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        col1 = new int[RowCount];
        col2 = new int[RowCount];
        col3 = new int[RowCount];

        var random = new Random(42);
        for (int i = 0; i < RowCount; i++)
        {
            col1[i] = random.Next();
            col2[i] = random.Next();
            col3[i] = random.Next();
        }
    }

    /// <summary>
    /// Columnar storage: Sequential access to each column.
    /// Perfect for cache and SIMD!
    /// </summary>
    [Benchmark(Description = "Columnar - Column-by-Column")]
    public long Columnar_ColumnByColumn()
    {
        long result = 0;
        for (int i = 0; i < RowCount; i++)
        {
            result += col1[i];
        }
        for (int i = 0; i < RowCount; i++)
        {
            result += col2[i];
        }
        for (int i = 0; i < RowCount; i++)
        {
            result += col3[i];
        }
        return result;
    }

    /// <summary>
    /// Columnar storage: Interleaved access (row-wise simulation).
    /// Tests temporal locality.
    /// </summary>
    [Benchmark(Description = "Columnar - Interleaved (Row-wise)")]
    public long Columnar_Interleaved()
    {
        long result = 0;
        for (int i = 0; i < RowCount; i++)
        {
            result += col1[i] + col2[i] + col3[i];
        }
        return result;
    }

    /// <summary>
    /// Block-based access to columnar data.
    /// Balances cache locality with sequential access.
    /// </summary>
    [Benchmark(Description = "Columnar - Block Processing")]
    public long Columnar_BlockProcessing()
    {
        long result = 0;
        int blockSize = 8192;

        for (int block = 0; block < RowCount; block += blockSize)
        {
            int blockEnd = Math.Min(block + blockSize, RowCount);

            // Process block from each column
            for (int i = block; i < blockEnd; i++)
            {
                result += col1[i] + col2[i] + col3[i];
            }
        }

        return result;
    }
}

/// <summary>
/// Phase 2E Wednesday: Tiled Matrix Processing
/// 
/// Tests 2D cache optimization with tiling.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_TiledMatrixBenchmark
{
    private int[] matrix = null!;
    private const int MATRIX_SIZE = 1024;
    private const int TILE_SIZE = 64;

    [GlobalSetup]
    public void Setup()
    {
        matrix = new int[MATRIX_SIZE * MATRIX_SIZE];
        var random = new Random(42);
        for (int i = 0; i < matrix.Length; i++)
        {
            matrix[i] = random.Next();
        }
    }

    /// <summary>
    /// Sequential matrix processing (row-major).
    /// Poor cache behavior for large matrices.
    /// </summary>
    [Benchmark(Description = "Matrix - Row-Major Sequential")]
    public long Matrix_RowMajorSequential()
    {
        long sum = 0;
        for (int i = 0; i < MATRIX_SIZE; i++)
        {
            for (int j = 0; j < MATRIX_SIZE; j++)
            {
                sum += matrix[i * MATRIX_SIZE + j];
            }
        }
        return sum;
    }

    /// <summary>
    /// Tiled matrix processing.
    /// Processes tiles that fit in cache.
    /// Expected: 1.5-1.8x faster!
    /// </summary>
    [Benchmark(Description = "Matrix - Tiled Processing")]
    public long Matrix_TiledProcessing()
    {
        return CacheOptimizer.ProcessTiledMatrix(matrix, MATRIX_SIZE, MATRIX_SIZE, TILE_SIZE);
    }

    /// <summary>
    /// Column-major access (poor for row-major storage).
    /// Shows how bad cache behavior can be.
    /// </summary>
    [Benchmark(Description = "Matrix - Column-Major Sequential")]
    public long Matrix_ColumnMajorSequential()
    {
        long sum = 0;
        for (int j = 0; j < MATRIX_SIZE; j++)
        {
            for (int i = 0; i < MATRIX_SIZE; i++)
            {
                sum += matrix[i * MATRIX_SIZE + j];
            }
        }
        return sum;
    }
}

/// <summary>
/// Phase 2E Wednesday: Cache Line Alignment
/// 
/// Tests impact of proper cache line alignment.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_CacheLineAlignmentBenchmark
{
    private CacheLineAlignedInt[] alignedData = null!;
    private int[] unalignedData = null!;
    private const int DataSize = 100000;

    [GlobalSetup]
    public void Setup()
    {
        alignedData = new CacheLineAlignedInt[DataSize];
        unalignedData = new int[DataSize * 8];

        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            alignedData[i].Value1 = random.Next();
            alignedData[i].Value2 = random.Next();
            alignedData[i].Value3 = random.Next();
            alignedData[i].Value4 = random.Next();
        }
        for (int i = 0; i < unalignedData.Length; i++)
        {
            unalignedData[i] = random.Next();
        }
    }

    /// <summary>
    /// Cache-line aligned access.
    /// </summary>
    [Benchmark(Description = "Alignment - Cache-Line Aligned")]
    public long Alignment_CacheLineAligned()
    {
        long sum = 0;
        for (int i = 0; i < alignedData.Length; i++)
        {
            sum += alignedData[i].Value1;
            sum += alignedData[i].Value2;
            sum += alignedData[i].Value3;
            sum += alignedData[i].Value4;
        }
        return sum;
    }

    /// <summary>
    /// Unaligned access pattern.
    /// </summary>
    [Benchmark(Description = "Alignment - Unaligned")]
    public long Alignment_Unaligned()
    {
        long sum = 0;
        for (int i = 0; i < unalignedData.Length; i++)
        {
            sum += unalignedData[i];
        }
        return sum;
    }
}

/// <summary>
/// Phase 2E Wednesday: Working Set Size Impact
/// 
/// Tests how data size affects cache performance.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_WorkingSetBenchmark
{
    private int[] smallData = null!;       // < L1 cache
    private int[] mediumData = null!;      // L2 cache size
    private int[] largeData = null!;       // > L3 cache

    [GlobalSetup]
    public void Setup()
    {
        // Small: 16KB (fits in L1)
        smallData = new int[4096];

        // Medium: 256KB (fits in L2)
        mediumData = new int[65536];

        // Large: 10MB (doesn't fit in L3)
        largeData = new int[2500000];

        var random = new Random(42);
        for (int i = 0; i < smallData.Length; i++)
            smallData[i] = random.Next();
        for (int i = 0; i < mediumData.Length; i++)
            mediumData[i] = random.Next();
        for (int i = 0; i < largeData.Length; i++)
            largeData[i] = random.Next();
    }

    /// <summary>
    /// L1-cache-fit data: Fastest!
    /// </summary>
    [Benchmark(Description = "WorkingSet - L1 Cache (16KB)")]
    public long WorkingSet_L1()
    {
        long sum = 0;
        for (int i = 0; i < smallData.Length; i++)
            sum += smallData[i];
        return sum;
    }

    /// <summary>
    /// L2-cache-fit data: Fast
    /// </summary>
    [Benchmark(Description = "WorkingSet - L2 Cache (256KB)")]
    public long WorkingSet_L2()
    {
        long sum = 0;
        for (int i = 0; i < mediumData.Length; i++)
            sum += mediumData[i];
        return sum;
    }

    /// <summary>
    /// Data too large for L3: Slow (memory bound)
    /// </summary>
    [Benchmark(Description = "WorkingSet - Main Memory (10MB)")]
    public long WorkingSet_Memory()
    {
        long sum = 0;
        for (int i = 0; i < largeData.Length; i++)
            sum += largeData[i];
        return sum;
    }
}
