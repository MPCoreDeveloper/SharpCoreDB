using BenchmarkDotNet.Attributes;
using SharpCoreDB.Optimization;
using System;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2E Friday: Hardware Optimization Benchmarks
/// 
/// Tests NUMA awareness, CPU affinity, and platform-specific optimizations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2E_HardwareOptimizationBenchmark
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
    /// Baseline: No affinity, random NUMA placement.
    /// Represents worst-case scheduling.
    /// </summary>
    [Benchmark(Description = "Hardware - No Optimization (Baseline)")]
    public long NoOptimization()
    {
        long sum = 0;
        for (int i = 0; i < testData.Length; i++)
        {
            sum += testData[i];
        }
        return sum;
    }

    /// <summary>
    /// Optimized: With CPU affinity.
    /// Thread pinned to specific core.
    /// Expected: 1.1-1.2x faster
    /// </summary>
    [Benchmark(Description = "Hardware - With CPU Affinity")]
    public long WithCPUAffinity()
    {
        // Pin to CPU 0 for consistency
        HardwareOptimizer.SetThreadAffinity(0);

        long sum = 0;
        for (int i = 0; i < testData.Length; i++)
        {
            sum += testData[i];
        }

        return sum;
    }

    /// <summary>
    /// Optimized: With NUMA awareness.
    /// Allocate on local NUMA node.
    /// Expected: 1.2-1.3x faster on multi-socket systems
    /// </summary>
    [Benchmark(Description = "Hardware - With NUMA Awareness")]
    public long WithNUMAAwareness()
    {
        var hardwareInfo = HardwareOptimizer.GetHardwareInfo();

        if (hardwareInfo.IsNUMAAvailable)
        {
            // Allocate on NUMA node 0
            var localData = HardwareOptimizer.AllocateOnNUMANode<int>(DataSize, 0);
            Array.Copy(testData, localData, DataSize);

            // Run on same NUMA node
            long sum = 0;
            for (int i = 0; i < localData.Length; i++)
            {
                sum += localData[i];
            }

            return sum;
        }
        else
        {
            // NUMA not available, fallback to regular
            return NoOptimization();
        }
    }

    /// <summary>
    /// Combined: Both affinity and NUMA.
    /// Full hardware optimization.
    /// Expected: 1.5-1.7x faster!
    /// </summary>
    [Benchmark(Description = "Hardware - Full Optimization")]
    public long FullOptimization()
    {
        var hardwareInfo = HardwareOptimizer.GetHardwareInfo();

        // Set CPU affinity
        HardwareOptimizer.SetThreadAffinity(0);

        // Use NUMA-aware allocation if available
        int[] dataToUse = testData;
        if (hardwareInfo.IsNUMAAvailable)
        {
            dataToUse = HardwareOptimizer.AllocateOnNUMANode<int>(DataSize, 0);
            Array.Copy(testData, dataToUse, DataSize);
        }

        long sum = 0;
        for (int i = 0; i < dataToUse.Length; i++)
        {
            sum += dataToUse[i];
        }

        return sum;
    }
}

/// <summary>
/// Phase 2E Friday: Platform-Specific Benchmarks
/// 
/// Tests platform detection and optimal code path selection.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_PlatformOptimizationBenchmark
{
    private int[] data1 = null!;
    private int[] data2 = null!;
    private const int DataSize = 1000000;

    [GlobalSetup]
    public void Setup()
    {
        data1 = new int[DataSize];
        data2 = new int[DataSize];

        var random = new Random(42);
        for (int i = 0; i < DataSize; i++)
        {
            data1[i] = random.Next();
            data2[i] = random.Next();
        }
    }

    /// <summary>
    /// Generic scalar multiplication (no SIMD).
    /// Slowest but universally compatible.
    /// </summary>
    [Benchmark(Description = "Platform - Scalar Only")]
    public long Platform_ScalarOnly()
    {
        long result = 0;
        for (int i = 0; i < DataSize; i++)
        {
            result += (long)data1[i] * data2[i];
        }
        return result;
    }

    /// <summary>
    /// Platform-detected optimal code.
    /// Uses best available SIMD or scalar.
    /// Expected: 1.5-2x faster on SIMD systems
    /// </summary>
    [Benchmark(Description = "Platform - Optimal Path")]
    public long Platform_OptimalPath()
    {
        var hardwareInfo = HardwareOptimizer.GetHardwareInfo();

        if (hardwareInfo.HasAVX2 || hardwareInfo.HasNEON)
        {
            // SIMD path would be used in real implementation
            // For now, simulate with unrolled version
            return Platform_UnrolledOptimization();
        }
        else
        {
            return Platform_ScalarOnly();
        }
    }

    /// <summary>
    /// Unrolled scalar (simulates SIMD benefit).
    /// </summary>
    private long Platform_UnrolledOptimization()
    {
        long result = 0;

        int i = 0;
        for (; i < DataSize - 3; i += 4)
        {
            result += (long)data1[i] * data2[i];
            result += (long)data1[i + 1] * data2[i + 1];
            result += (long)data1[i + 2] * data2[i + 2];
            result += (long)data1[i + 3] * data2[i + 3];
        }

        while (i < DataSize)
        {
            result += (long)data1[i] * data2[i];
            i++;
        }

        return result;
    }
}

/// <summary>
/// Phase 2E Friday: NUMA Scalability Benchmark
/// 
/// Tests performance improvement across NUMA nodes.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_NUMAScalabilityBenchmark
{
    private int[] data = null!;
    private const int DataSize = 5000000;

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
    /// Baseline: Default allocation and scheduling.
    /// </summary>
    [Benchmark(Description = "NUMA - Baseline")]
    public long NUMA_Baseline()
    {
        long sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];
        }
        return sum;
    }

    /// <summary>
    /// Single-threaded with NUMA optimization.
    /// </summary>
    [Benchmark(Description = "NUMA - Single Thread Optimized")]
    public long NUMA_SingleThreadOptimized()
    {
        HardwareOptimizer.SetThreadAffinity(0);
        return NUMA_Baseline();  // Now runs with affinity
    }

    /// <summary>
    /// Reports hardware info.
    /// </summary>
    [Benchmark(Description = "NUMA - Hardware Detection")]
    public string NUMA_HardwareDetection()
    {
        var info = HardwareOptimizer.GetHardwareInfo();
        return info.ToString();
    }
}

/// <summary>
/// Phase 2E Friday: CPU Affinity Impact
/// 
/// Demonstrates context switch reduction through affinity.
/// </summary>
[MemoryDiagnoser]
public class Phase2E_CPUAffinityBenchmark
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
            data[i] = random.Next();
        }
    }

    /// <summary>
    /// No affinity: Thread can migrate between cores.
    /// Cache invalidation on each migration.
    /// </summary>
    [Benchmark(Description = "Affinity - No Affinity (Baseline)")]
    public long Affinity_NoAffinity()
    {
        long sum = 0;
        for (int i = 0; i < DataSize; i++)
        {
            sum += data[i];
        }
        return sum;
    }

    /// <summary>
    /// With affinity: Thread pinned to core 0.
    /// Cache stays warm across iterations.
    /// Expected: 1.1-1.2x faster
    /// </summary>
    [Benchmark(Description = "Affinity - Core 0 Pinned")]
    public long Affinity_CorePinned()
    {
        HardwareOptimizer.SetThreadAffinity(0);
        return Affinity_NoAffinity();
    }

    /// <summary>
    /// Multiple iterations: Shows affinity benefit grows.
    /// </summary>
    [Benchmark(Description = "Affinity - Repeated Access")]
    public long Affinity_RepeatedAccess()
    {
        HardwareOptimizer.SetThreadAffinity(0);

        long sum = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < DataSize; i++)
            {
                sum += data[i];
            }
        }
        return sum;
    }
}

/// <summary>
/// Phase 2E Friday: Comprehensive Hardware Report
/// 
/// Generates detailed hardware analysis.
/// </summary>
[SimpleJob(warmupCount: 1, iterationCount: 1)]
public class Phase2E_HardwareReportBenchmark
{
    [Benchmark(Description = "Report - Hardware Capabilities")]
    public string Report_HardwareCapabilities()
    {
        var info = HardwareOptimizer.GetHardwareInfo();
        return $"Processors: {info.ProcessorCount}, " +
               $"NUMA: {(info.IsNUMAAvailable ? info.NUMANodeCount : "N/A")}, " +
               $"AVX-512: {info.HasAVX512}, AVX2: {info.HasAVX2}, " +
               $"OS: {(info.IsWindows ? "Windows" : info.IsLinux ? "Linux" : "macOS")}";
    }

    [Benchmark(Description = "Report - Optimal Settings")]
    public string Report_OptimalSettings()
    {
        var parallelism = HardwareOptimizer.GetOptimalParallelism();
        var vectorSize = PlatformOptimizer.GetOptimalVectorSize();
        var containerized = HardwareOptimizer.IsContainerized();
        
        return $"Parallelism: {parallelism}, Vector Size: {vectorSize}, Containerized: {containerized}";
    }
}
