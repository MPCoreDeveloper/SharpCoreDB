using BenchmarkDotNet.Attributes;
using SharpCoreDB.Services;
using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2D Monday: Modern SIMD Optimization Benchmarks
/// 
/// Compares traditional scalar operations vs modern Vector128/Vector256 operations.
/// Uses .NET 10 optimized Vector APIs.
/// 
/// Expected improvements:
/// - Vector256 sum: 2-3x faster
/// - Comparison: 2-3x faster
/// - Multiply-add: 2-3x faster
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2D_ModernSimdBenchmark
{
    private int[] testData = null!;
    private int[] testData2 = null!;
    private byte[] resultBuffer = null!;
    private long[] multiplyAddResults = null!;
    
    private const int DataSize = 10000;

    [GlobalSetup]
    public void Setup()
    {
        testData = new int[DataSize];
        testData2 = new int[DataSize];
        resultBuffer = new byte[DataSize];
        multiplyAddResults = new long[DataSize];
        
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            testData[i] = random.Next(-1000, 1000);
            testData2[i] = random.Next(-1000, 1000);
            multiplyAddResults[i] = 0;
        }
    }

    /// <summary>
    /// Baseline: Scalar sum (traditional approach)
    /// </summary>
    [Benchmark(Description = "Sum - Scalar")]
    public long Sum_Scalar()
    {
        long sum = 0;
        foreach (var value in testData)
        {
            sum += value;
        }
        return sum;
    }

    /// <summary>
    /// Modern: Vector256 sum using modern SIMD
    /// Expected: 2-3x faster
    /// </summary>
    [Benchmark(Description = "Sum - Modern SIMD Vector256")]
    public long Sum_ModernSimdVector256()
    {
        return ModernSimdOptimizer.ModernHorizontalSum(testData);
    }

    /// <summary>
    /// Baseline: Scalar comparison
    /// </summary>
    [Benchmark(Description = "Compare - Scalar")]
    public int Compare_Scalar()
    {
        int count = 0;
        int threshold = 500;
        
        for (int i = 0; i < testData.Length; i++)
        {
            resultBuffer[i] = (byte)(testData[i] > threshold ? 1 : 0);
            if (testData[i] > threshold)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Modern: Vector256 comparison
    /// Expected: 2-3x faster
    /// </summary>
    [Benchmark(Description = "Compare - Modern SIMD Vector256")]
    public int Compare_ModernSimdVector256()
    {
        return ModernSimdOptimizer.ModernCompareGreaterThan(testData, 500, resultBuffer);
    }

    /// <summary>
    /// Baseline: Scalar multiply-add
    /// </summary>
    [Benchmark(Description = "MultiplyAdd - Scalar")]
    public long MultiplyAdd_Scalar()
    {
        for (int i = 0; i < testData.Length; i++)
        {
            multiplyAddResults[i] += (long)testData[i] * testData2[i];
        }
        
        long sum = 0;
        foreach (var value in multiplyAddResults)
            sum += value;
        return sum;
    }

    /// <summary>
    /// Modern: Vector256 multiply-add
    /// Expected: 2-3x faster
    /// </summary>
    [Benchmark(Description = "MultiplyAdd - Modern SIMD Vector256")]
    public long MultiplyAdd_ModernSimdVector256()
    {
        ModernSimdOptimizer.ModernMultiplyAdd(testData, testData2, multiplyAddResults);
        
        long sum = 0;
        foreach (var value in multiplyAddResults)
            sum += value;
        return sum;
    }

    /// <summary>
    /// Test SIMD capability detection
    /// </summary>
    [Benchmark(Description = "SIMD Capability Check")]
    public bool SimdCapabilityCheck()
    {
        return ModernSimdOptimizer.SupportsModernSimd;
    }
}

/// <summary>
/// Phase 2D Monday: Cache-Aware SIMD Processing Benchmarks
/// 
/// Tests cache efficiency improvements from aligned processing.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_CacheAwareSimdBenchmark
{
    private int[] largeData = null!;
    private const int LargeDataSize = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        largeData = new int[LargeDataSize];
        var random = new Random(42);
        for (int i = 0; i < LargeDataSize; i++)
        {
            largeData[i] = random.Next();
        }
    }

    /// <summary>
    /// Large data sum - scalar (baseline)
    /// Tests cache efficiency without SIMD
    /// </summary>
    [Benchmark(Description = "Large Data Sum - Scalar")]
    public long LargeDataSum_Scalar()
    {
        long sum = 0;
        foreach (var value in largeData)
        {
            sum += value;
        }
        return sum;
    }

    /// <summary>
    /// Large data sum - modern SIMD with cache awareness
    /// Expected: 2-3x faster due to better cache utilization
    /// </summary>
    [Benchmark(Description = "Large Data Sum - Modern SIMD")]
    public long LargeDataSum_ModernSimd()
    {
        return ModernSimdOptimizer.ModernHorizontalSum(largeData);
    }

    /// <summary>
    /// Multiple passes over data
    /// Tests SIMD efficiency for repeated operations
    /// </summary>
    [Benchmark(Description = "Multiple Pass Sum - SIMD")]
    public long MultiplePassSum_Simd()
    {
        long total = 0;
        
        // 5 passes over data
        for (int pass = 0; pass < 5; pass++)
        {
            total += ModernSimdOptimizer.ModernHorizontalSum(largeData);
        }
        
        return total;
    }
}

/// <summary>
/// Phase 2D Monday: Vector Instruction Throughput Benchmark
/// 
/// Measures actual instruction throughput and latency.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_VectorThroughputBenchmark
{
    private int[] data = null!;
    private const int DataSize = 100000;

    [GlobalSetup]
    public void Setup()
    {
        data = new int[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            data[i] = random.Next(-100, 100);
        }
    }

    /// <summary>
    /// Throughput test: Many independent operations
    /// Tests CPU ability to execute multiple operations in parallel
    /// </summary>
    [Benchmark(Description = "Vector Throughput - Multiple Ops")]
    public int VectorThroughput_MultipleOps()
    {
        long sum1 = 0, sum2 = 0, sum3 = 0, sum4 = 0;

        // Process data 4 times in parallel accumulators
        // Modern CPU executes these in parallel (no dependencies)
        for (int i = 0; i < data.Length; i += 4)
        {
            sum1 += data[i];
            sum2 += data[i + 1];
            sum3 += data[i + 2];
            sum4 += data[i + 3];
        }

        // Return total to prevent optimization
        return (int)((sum1 + sum2 + sum3 + sum4) & 0xFF);
    }

    /// <summary>
    /// Modern SIMD should achieve similar or better results
    /// while handling larger chunks
    /// </summary>
    [Benchmark(Description = "Vector Throughput - SIMD")]
    public int VectorThroughput_Simd()
    {
        if (!ModernSimdOptimizer.SupportsModernSimd)
            return 0;

        long sum = ModernSimdOptimizer.ModernHorizontalSum(data);
        return (int)(sum & 0xFF);
    }

    /// <summary>
    /// Latency test: Sequential operations
    /// Each operation depends on previous result
    /// </summary>
    [Benchmark(Description = "Vector Latency - Sequential")]
    public int VectorLatency_Sequential()
    {
        int result = 0;
        
        for (int i = 0; i < data.Length; i++)
        {
            result = (result + data[i]) ^ 0xFF;  // Dependency: can't parallelize
        }
        
        return result;
    }
}

/// <summary>
/// Phase 2D Monday: Memory Bandwidth Efficiency Benchmark
/// 
/// Measures how efficiently SIMD uses memory bandwidth.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_MemoryBandwidthBenchmark
{
    private int[] source = null!;
    private int[] destination = null!;
    private const int DataSize = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        source = new int[DataSize];
        destination = new int[DataSize];
        
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            source[i] = random.Next();
        }
    }

    /// <summary>
    /// Scalar memory copy: One value at a time
    /// Baseline memory bandwidth usage
    /// </summary>
    [Benchmark(Description = "Memory Copy - Scalar")]
    public int MemoryCopy_Scalar()
    {
        Array.Copy(source, destination, source.Length);
        return destination.Length;
    }

    /// <summary>
    /// SIMD memory operations: Bulk transfer
    /// Modern Vector<T> can copy 32 bytes at a time
    /// </summary>
    [Benchmark(Description = "Memory Copy - Vector256 Block")]
    public int MemoryCopy_Vector256Block()
    {
        // Manual block copy using larger chunks
        int i = 0;
        const int BlockSize = 256;
        
        for (; i <= source.Length - BlockSize; i += BlockSize)
        {
            Array.Copy(source, i, destination, i, BlockSize);
        }
        
        // Remainder
        if (i < source.Length)
        {
            Array.Copy(source, i, destination, i, source.Length - i);
        }
        
        return destination.Length;
    }
}
