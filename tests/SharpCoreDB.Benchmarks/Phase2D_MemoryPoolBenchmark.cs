using BenchmarkDotNet.Attributes;
using SharpCoreDB.Memory;
using System;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2D Wednesday: Memory Pool Benchmarks
/// 
/// Compares pooled vs non-pooled allocation patterns.
/// Measures actual performance improvements and GC impact.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2D_MemoryPoolBenchmark
{
    private const int IterationCount = 10000;
    private ObjectPool<TestObject> objectPool = null!;
    private BufferPool bufferPool = null!;

    [GlobalSetup]
    public void Setup()
    {
        objectPool = new ObjectPool<TestObject>(
            maxPoolSize: 1000,
            resetAction: obj => obj.Reset()
        );

        bufferPool = new BufferPool(maxBuffersPerSize: 512);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        objectPool.Clear();
        bufferPool.Clear();
    }

    /// <summary>
    /// Baseline: Direct allocation (no pooling)
    /// </summary>
    [Benchmark(Description = "Direct Allocation - No Pooling")]
    public void DirectAllocation_NoPooling()
    {
        for (int i = 0; i < IterationCount; i++)
        {
            var obj = new TestObject();
            obj.DoWork();
        }
    }

    /// <summary>
    /// Optimized: Using ObjectPool
    /// Expected: 2-4x faster due to reuse
    /// </summary>
    [Benchmark(Description = "Object Pooling - Reused")]
    public void ObjectPooling_Reused()
    {
        for (int i = 0; i < IterationCount; i++)
        {
            var obj = objectPool.Rent();
            try
            {
                obj.DoWork();
            }
            finally
            {
                objectPool.Return(obj);
            }
        }
    }

    /// <summary>
    /// Baseline: Direct buffer allocation
    /// </summary>
    [Benchmark(Description = "Buffer Allocation - Direct")]
    public void BufferAllocation_Direct()
    {
        for (int i = 0; i < IterationCount; i++)
        {
            byte[] buffer = new byte[4096];
            ProcessBuffer(buffer);
        }
    }

    /// <summary>
    /// Optimized: Using BufferPool
    /// Expected: 2-3x faster
    /// </summary>
    [Benchmark(Description = "Buffer Pooling - Reused")]
    public void BufferPooling_Reused()
    {
        for (int i = 0; i < IterationCount; i++)
        {
            byte[] buffer = bufferPool.Rent(4096);
            try
            {
                ProcessBuffer(buffer);
            }
            finally
            {
                bufferPool.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Tests various buffer sizes with pooling
    /// </summary>
    [Benchmark(Description = "Mixed Buffer Sizes - Pooled")]
    public void MixedBufferSizes_Pooled()
    {
        for (int i = 0; i < IterationCount / 10; i++)
        {
            // Various sizes to test power-of-2 bucketing
            for (int size = 256; size <= 8192; size *= 2)
            {
                byte[] buffer = bufferPool.Rent(size);
                try
                {
                    ProcessBuffer(buffer);
                }
                finally
                {
                    bufferPool.Return(buffer);
                }
            }
        }
    }

    /// <summary>
    /// Helper: Process buffer (simulate work)
    /// </summary>
    private static void ProcessBuffer(byte[] buffer)
    {
        // Simple processing
        int sum = 0;
        for (int i = 0; i < Math.Min(buffer.Length, 1024); i++)
        {
            sum += buffer[i];
        }
    }

    /// <summary>
    /// Test class for object pooling
    /// </summary>
    private class TestObject
    {
        public int Value { get; set; }
        public byte[] Data { get; set; } = new byte[256];

        public void DoWork()
        {
            Value++;
            for (int i = 0; i < Data.Length; i++)
            {
                Data[i] = (byte)(Value & 0xFF);
            }
        }

        public void Reset()
        {
            Value = 0;
            Array.Clear(Data, 0, Data.Length);
        }
    }
}

/// <summary>
/// Phase 2D Wednesday: GC Pressure Benchmarks
/// 
/// Measures garbage collection pressure reduction with pooling.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_GCPressureBenchmark
{
    private const int OperationCount = 50000;
    private ObjectPool<DataBuffer> pool = null!;

    [GlobalSetup]
    public void Setup()
    {
        pool = new ObjectPool<DataBuffer>(maxPoolSize: 500);
    }

    /// <summary>
    /// Baseline: Without pooling
    /// Measures allocation count and memory usage
    /// </summary>
    [Benchmark(Description = "GC Pressure - Direct Allocation")]
    public long GCPressure_DirectAllocation()
    {
        long totalBytes = 0;

        for (int i = 0; i < OperationCount; i++)
        {
            var buffer = new DataBuffer();
            buffer.Fill();
            totalBytes += buffer.GetSize();
        }

        return totalBytes;
    }

    /// <summary>
    /// Optimized: With pooling
    /// Expected: Dramatically lower allocation count
    /// </summary>
    [Benchmark(Description = "GC Pressure - With Pooling")]
    public long GCPressure_WithPooling()
    {
        long totalBytes = 0;

        for (int i = 0; i < OperationCount; i++)
        {
            var buffer = pool.Rent();
            try
            {
                buffer.Fill();
                totalBytes += buffer.GetSize();
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        return totalBytes;
    }

    /// <summary>
    /// Data buffer for testing
    /// </summary>
    private class DataBuffer
    {
        public byte[] Data { get; } = new byte[1024];

        public void Fill()
        {
            for (int i = 0; i < Data.Length; i++)
            {
                Data[i] = (byte)(i % 256);
            }
        }

        public int GetSize() => Data.Length;
    }
}

/// <summary>
/// Phase 2D Wednesday: Pool Statistics Benchmark
/// 
/// Demonstrates pool statistics and reuse rate tracking.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_PoolStatisticsBenchmark
{
    private ObjectPool<StatisticsTestObject> objectPool = null!;
    private BufferPool bufferPool = null!;

    [GlobalSetup]
    public void Setup()
    {
        objectPool = new ObjectPool<StatisticsTestObject>(maxPoolSize: 100);
        bufferPool = new BufferPool(maxBuffersPerSize: 100);
    }

    /// <summary>
    /// Track object pool statistics during warm-up
    /// </summary>
    [Benchmark(Description = "Object Pool - Warm-up Statistics")]
    public void ObjectPoolWarmup()
    {
        // First pass: cold
        for (int i = 0; i < 100; i++)
        {
            var obj = objectPool.Rent();
            obj.DoWork();
            objectPool.Return(obj);
        }

        var stats1 = objectPool.GetStatistics();
        // After warm-up: expect ~100% reuse

        // Second pass: warm
        for (int i = 0; i < 100; i++)
        {
            var obj = objectPool.Rent();
            obj.DoWork();
            objectPool.Return(obj);
        }

        var stats2 = objectPool.GetStatistics();
        // Reuse rate should be very high (>99%)
    }

    /// <summary>
    /// Track buffer pool statistics
    /// </summary>
    [Benchmark(Description = "Buffer Pool - Warm-up Statistics")]
    public void BufferPoolWarmup()
    {
        // First pass: cold
        for (int i = 0; i < 100; i++)
        {
            byte[] buffer = bufferPool.Rent(4096);
            Array.Clear(buffer, 0, buffer.Length);
            bufferPool.Return(buffer);
        }

        var stats1 = bufferPool.GetStatistics();

        // Second pass: warm (should see high reuse)
        for (int i = 0; i < 100; i++)
        {
            byte[] buffer = bufferPool.Rent(4096);
            Array.Clear(buffer, 0, buffer.Length);
            bufferPool.Return(buffer);
        }

        var stats2 = bufferPool.GetStatistics();
    }

    private class StatisticsTestObject
    {
        public int Value { get; set; }

        public void DoWork()
        {
            Value = (Value + 1) % 100;
        }
    }
}

/// <summary>
/// Phase 2D Wednesday: Concurrent Access Benchmark
/// 
/// Tests thread-safety and performance under concurrent load.
/// </summary>
[MemoryDiagnoser]
public class Phase2D_ConcurrentPoolBenchmark
{
    private ObjectPool<ConcurrentTestObject> objectPool = null!;
    private BufferPool bufferPool = null!;
    private const int ThreadCount = 8;
    private const int OperationsPerThread = 10000;

    [GlobalSetup]
    public void Setup()
    {
        objectPool = new ObjectPool<ConcurrentTestObject>(maxPoolSize: ThreadCount * 2);
        bufferPool = new BufferPool(maxBuffersPerSize: ThreadCount * 2);
    }

    /// <summary>
    /// Concurrent object pool access
    /// </summary>
    [Benchmark(Description = "Object Pool - Concurrent Access")]
    public void ConcurrentObjectPooling()
    {
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

        for (int t = 0; t < ThreadCount; t++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    var obj = objectPool.Rent();
                    try
                    {
                        obj.Value = i;
                    }
                    finally
                    {
                        objectPool.Return(obj);
                    }
                }
            }));
        }

        System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
    }

    /// <summary>
    /// Concurrent buffer pool access
    /// </summary>
    [Benchmark(Description = "Buffer Pool - Concurrent Access")]
    public void ConcurrentBufferPooling()
    {
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

        for (int t = 0; t < ThreadCount; t++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < OperationsPerThread; i++)
                {
                    byte[] buffer = bufferPool.Rent(4096);
                    try
                    {
                        buffer[0] = (byte)(i % 256);
                    }
                    finally
                    {
                        bufferPool.Return(buffer);
                    }
                }
            }));
        }

        System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
    }

    private class ConcurrentTestObject
    {
        public int Value { get; set; }
    }
}
