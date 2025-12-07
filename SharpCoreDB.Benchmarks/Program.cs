using SharpCoreDB.Benchmarks;

// Check if benchmarks are requested via command-line args
if (args.Length > 0 && args[0].Contains("MemoryMapped"))
{
    // Run memory-mapped files benchmark (100k records)
    MemoryMappedFilesBenchmark.RunMemoryMappedFilesBenchmark();
}
else if (args.Length > 0 && args[0].Contains("Validate"))
{
    // Quick validation of all three optimizations (10k records, fast)
    QuickValidationBench.RunQuickValidation();
}
else if (args.Length > 0 && args[0].Contains("Optimizations"))
{
    // Run the comprehensive optimizations benchmark (QueryCache, HashIndex, GC) - 100k records, slow
    OptimizationsBenchmark.RunOptimizationsBenchmark();
}
else if (args.Length > 0 && args[0].Contains("ComprehensiveBench"))
{
    // Note: BenchmarkDotNet removed for .NET 10 compatibility
    Console.WriteLine("ComprehensiveBenchmark requires BenchmarkDotNet, not available for .NET 10 yet.");
}
else if (args.Length > 0 && args[0].Contains("Comprehensive"))
{
    var count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 10000;
    // ComprehensivePerformanceTest.RunPerformanceTest(count);
}
else if (args.Length > 0 && args[0].Contains("NoEncryptionBench"))
{
    // Note: BenchmarkDotNet removed for .NET 10 compatibility
    Console.WriteLine("NoEncryptionBenchmarks requires BenchmarkDotNet, not available for .NET 10 yet.");
}
else if (args.Length > 0 && args[0].Contains("NoEncryption"))
{
    NoEncryptionPerformanceTest.RunPerformanceTest();
}
else if (args.Length > 0 && args[0].Contains("TimeTracking"))
{
    // Note: BenchmarkDotNet removed for .NET 10 compatibility
    Console.WriteLine("TimeTrackingBenchmarks requires BenchmarkDotNet, not available for .NET 10 yet.");
}
else if (args.Length > 0 && args[0].Contains("QueryCache"))
{
    // Run QueryCacheBenchmark
    var benchmark = new QueryCacheBenchmark();
    benchmark.Setup();
    try
    {
        Console.WriteLine("Running SharpCoreDB Cached Parameterized Select...");
        var cachedTime = benchmark.SharpCoreDB_Cached_ParameterizedSelect();
        Console.WriteLine($"Time: {cachedTime} ms");

        Console.WriteLine("Running SharpCoreDB No Cache Parameterized Select...");
        var noCacheTime = benchmark.SharpCoreDB_NoCache_ParameterizedSelect();
        Console.WriteLine($"Time: {noCacheTime} ms");

        Console.WriteLine("Running Concurrent Async Selects...");
        var cachedAsyncTime = await benchmark.SharpCoreDB_Cached_ConcurrentAsyncSelect();
        Console.WriteLine($"Cached Async Time: {cachedAsyncTime} ms");

        var noCacheAsyncTime = await benchmark.SharpCoreDB_NoCache_ConcurrentAsyncSelect();
        Console.WriteLine($"No Cache Async Time: {noCacheAsyncTime} ms");

        benchmark.LogExplainPlans();
        benchmark.ExportResultsToMarkdown();
    }
    finally
    {
        benchmark.Cleanup();
    }
}
else
{
    // Run simple performance test by default
    // PerformanceTest.RunPerformanceTest();
}
