using BenchmarkDotNet.Attributes;
using SharpCoreDB.Optimization;
using System;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2E Monday: JIT Optimization & Loop Unrolling Benchmarks
/// 
/// Demonstrates instruction-level parallelism improvement through loop unrolling.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2E_JitOptimizationBenchmark
{
    private int[] testData = null!;
    private long[] longData = null!;
    private const int DataSize = 100000;

    [GlobalSetup]
    public void Setup()
    {
        testData = new int[DataSize];
        longData = new long[DataSize];

        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            testData[i] = random.Next(-10000, 10000);
            longData[i] = random.Next(-10000, 10000);
        }
    }

    /// <summary>
    /// Baseline: Sequential sum (no unrolling)
    /// Represents what JIT generates without hints.
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
    /// Optimized: Unroll-2
    /// Exposes 2x parallelism.
    /// </summary>
    [Benchmark(Description = "Sum - Unroll-2")]
    public long Sum_Unroll2()
    {
        return JitOptimizer.Sum_Unroll2(testData);
    }

    /// <summary>
    /// Optimized: Unroll-4
    /// Exposes 4x parallelism.
    /// </summary>
    [Benchmark(Description = "Sum - Unroll-4")]
    public long Sum_Unroll4()
    {
        return JitOptimizer.Sum_Unroll4(testData);
    }

    /// <summary>
    /// Optimized: Unroll-4 with multiple accumulators
    /// Reduces dependency chains, improves register allocation.
    /// Expected: Best performance!
    /// </summary>
    [Benchmark(Description = "Sum - Unroll-4 (Multi-Accumulator)")]
    public long Sum_Unroll4_MultiAccumulator()
    {
        return JitOptimizer.Sum_Unroll4_MultiAccumulator(testData);
    }

    /// <summary>
    /// Optimized: Unroll-8
    /// Exposes 8x parallelism, larger code size.
    /// </summary>
    [Benchmark(Description = "Sum - Unroll-8")]
    public long Sum_Unroll8()
    {
        return JitOptimizer.Sum_Unroll8(testData);
    }

    /// <summary>
    /// Optimized: Unroll-8 with quad accumulators
    /// Maximum parallelism with 4 independent accumulators.
    /// </summary>
    [Benchmark(Description = "Sum - Unroll-8 (Quad-Accumulator)")]
    public long Sum_Unroll8_QuadAccumulator()
    {
        return JitOptimizer.Sum_Unroll8_QuadAccumulator(testData);
    }

    /// <summary>
    /// Comparison: Sequential count
    /// Baseline for comparison operations.
    /// </summary>
    [Benchmark(Description = "Count - Sequential")]
    public int Count_Sequential()
    {
        int count = 0;
        int threshold = 5000;
        for (int i = 0; i < testData.Length; i++)
        {
            if (testData[i] > threshold)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Comparison: Unrolled-4 count
    /// Better branch prediction with unrolling.
    /// </summary>
    [Benchmark(Description = "Count - Unroll-4")]
    public int Count_Unroll4()
    {
        return JitOptimizer.Count_Unroll4(testData, 5000);
    }

    /// <summary>
    /// Multiply-accumulate sequential
    /// Baseline for fusion operations.
    /// </summary>
    [Benchmark(Description = "MultiplyAccumulate - Sequential")]
    public long MultiplyAccumulate_Sequential()
    {
        long sum = 0;
        for (int i = 0; i < testData.Length; i++)
        {
            sum += (long)testData[i] * testData[(i + 1) % testData.Length];
        }
        return sum;
    }

    /// <summary>
    /// Multiply-accumulate unrolled
    /// Better for FMA (fused multiply-add) instructions.
    /// </summary>
    [Benchmark(Description = "MultiplyAccumulate - Unroll-4")]
    public long MultiplyAccumulate_Unroll4()
    {
        return JitOptimizer.MultiplyAccumulate_Unroll4(testData, testData);
    }
}

/// <summary>
/// Phase 2E Monday: Parallel Reduction Benchmarks
/// 
/// Tests reduction operations with multiple accumulators.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_ParallelReductionBenchmark
{
    private long[] data = null!;
    private const int DataSize = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        data = new long[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            data[i] = random.Next(-100000, 100000);
        }
    }

    /// <summary>
    /// Sequential reduction (single accumulator)
    /// Baseline with dependency chains.
    /// </summary>
    [Benchmark(Description = "Reduction - Sequential")]
    public long Reduction_Sequential()
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        return sum;
    }

    /// <summary>
    /// Parallel reduction with 4 accumulators
    /// Reduces dependency chain latency.
    /// Expected: 1.5-1.8x faster!
    /// </summary>
    [Benchmark(Description = "Reduction - Parallel (4 Accumulators)")]
    public long Reduction_ParallelFourAccumulators()
    {
        return ReductionOptimizer.Sum_ParallelReduction(data);
    }

    /// <summary>
    /// Min/Max reduction sequential
    /// </summary>
    [Benchmark(Description = "MinMax - Sequential")]
    public (long, long) MinMax_Sequential()
    {
        long min = data[0], max = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] < min) min = data[i];
            if (data[i] > max) max = data[i];
        }
        return (min, max);
    }

    /// <summary>
    /// Min/Max reduction unrolled-4
    /// </summary>
    [Benchmark(Description = "MinMax - Unroll-4")]
    public (int, int) MinMax_Unroll4()
    {
        var intData = new int[data.Length];
        for (int i = 0; i < data.Length; i++)
            intData[i] = (int)data[i];
        return ReductionOptimizer.MinMax_Unroll4(intData);
    }
}

/// <summary>
/// Phase 2E Monday: Instruction-Level Parallelism Benchmark
/// 
/// Measures actual ILP improvement from unrolling.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_InstructionParallelismBenchmark
{
    private int[] data = null!;
    private const int DataSize = 500000;

    [GlobalSetup]
    public void Setup()
    {
        data = new int[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            data[i] = random.Next();
        }
    }

    /// <summary>
    /// Single independent chain (baseline)
    /// CPU can't parallelize.
    /// </summary>
    [Benchmark(Description = "ILP - Single Chain")]
    public long ILP_SingleChain()
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];  // Each iteration depends on previous sum
        }
        return sum;
    }

    /// <summary>
    /// Two independent chains (2x parallelism)
    /// CPU can execute both in parallel.
    /// </summary>
    [Benchmark(Description = "ILP - Dual Chain")]
    public long ILP_DualChain()
    {
        long sum1 = 0, sum2 = 0;
        for (int i = 0; i < data.Length - 1; i += 2)
        {
            sum1 += data[i];      // Chain 1
            sum2 += data[i + 1];  // Chain 2 (independent)
        }
        if (data.Length % 2 == 1)
            sum1 += data[data.Length - 1];
        return sum1 + sum2;
    }

    /// <summary>
    /// Four independent chains (4x parallelism)
    /// Maximum utilization of execution units.
    /// </summary>
    [Benchmark(Description = "ILP - Quad Chain")]
    public long ILP_QuadChain()
    {
        long sum1 = 0, sum2 = 0, sum3 = 0, sum4 = 0;
        int i = 0;
        for (; i < data.Length - 3; i += 4)
        {
            sum1 += data[i];      // Chain 1
            sum2 += data[i + 1];  // Chain 2
            sum3 += data[i + 2];  // Chain 3
            sum4 += data[i + 3];  // Chain 4
        }
        while (i < data.Length)
            sum1 += data[i++];
        return sum1 + sum2 + sum3 + sum4;
    }
}

/// <summary>
/// Phase 2E Monday: Branch Prediction Impact
/// 
/// Tests effect of branch prediction with unrolling.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_BranchPredictionBenchmark
{
    private int[] data = null!;
    private const int DataSize = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        data = new int[DataSize];
        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            data[i] = random.Next(-1000, 1000);
        }
    }

    /// <summary>
    /// High branch frequency (sequential)
    /// Branch mispredictions every iteration.
    /// </summary>
    [Benchmark(Description = "Branch - Sequential (High Frequency)")]
    public int BranchPrediction_Sequential()
    {
        int count = 0;
        int threshold = 500;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] > threshold)  // Branch every iteration
                count++;
        }
        return count;
    }

    /// <summary>
    /// Lower branch frequency (unrolled)
    /// Fewer branches = better prediction.
    /// </summary>
    [Benchmark(Description = "Branch - Unroll-4 (Low Frequency)")]
    public int BranchPrediction_Unroll4()
    {
        int count = 0;
        int threshold = 500;
        int i = 0;

        for (; i < data.Length - 3; i += 4)
        {
            if (data[i] > threshold) count++;
            if (data[i + 1] > threshold) count++;
            if (data[i + 2] > threshold) count++;
            if (data[i + 3] > threshold) count++;
        }

        while (i < data.Length)
        {
            if (data[i++] > threshold) count++;
        }

        return count;
    }
}
