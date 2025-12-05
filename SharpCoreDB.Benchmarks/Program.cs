using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks;

// Check if benchmarks are requested via command-line args
if (args.Length > 0 && args[0].Contains("Validate"))
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
    BenchmarkRunner.Run<ComprehensiveBenchmark>();
}
else if (args.Length > 0 && args[0].Contains("Comprehensive"))
{
    var count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 10000;
    ComprehensivePerformanceTest.RunPerformanceTest(count);
}
else if (args.Length > 0 && args[0].Contains("NoEncryptionBench"))
{
    BenchmarkRunner.Run<NoEncryptionBenchmarks>();
}
else if (args.Length > 0 && args[0].Contains("NoEncryption"))
{
    NoEncryptionPerformanceTest.RunPerformanceTest();
}
else if (args.Length > 0 && args[0].Contains("TimeTracking"))
{
    BenchmarkRunner.Run<TimeTrackingBenchmarks>();
}
else
{
    // Run simple performance test by default
    PerformanceTest.RunPerformanceTest();
}
