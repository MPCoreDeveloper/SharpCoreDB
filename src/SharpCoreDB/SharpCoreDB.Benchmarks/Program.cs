using System;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Entry point for running StructRow API benchmarks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    public static void Main(string[] args)
    {
        Console.WriteLine("SharpCoreDB StructRow API Benchmarks");
        Console.WriteLine("====================================\n");

        try
        {
            // Run basic benchmarks
            StructRowBenchmark.RunBenchmarks();

            // Run cross-engine comparison
            CrossEngineBenchmark.RunCrossEngineBenchmarks();

            Console.WriteLine("\n✅ Benchmarks completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Benchmark failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("  3) UPDATE performance - Priority 1 validation (UpdatePerformanceTest)");
        Console.WriteLine("  4) SELECT optimization - Phase-by-phase speedup (SelectOptimizationTest)");
        Console.WriteLine("  5) StructRow API - Zero-copy performance (StructRowBenchmark)");
        Console.WriteLine("  0) Exit");

        string? option = Console.ReadLine();
        using (var logWriter = new StreamWriter("benchmark_log.txt", true))
        {
            logWriter.AutoFlush = true;

            switch (option)
            {
                case "3":
                    Console.WriteLine("Running UpdatePerformanceTest (UPDATE Performance Validation)...");
                    logWriter.WriteLine("Running UpdatePerformanceTest...");
                    // UpdatePerformanceTest.Main(); // Not available
                    Console.WriteLine("UpdatePerformanceTest not implemented yet.");
                    logWriter.WriteLine("UpdatePerformanceTest not implemented yet.");
                    break;
                case "4":
                    Console.WriteLine("Running SelectOptimizationTest (SELECT Phase-by-Phase Speedup)...");
                    logWriter.WriteLine("Running SelectOptimizationTest...");
                    // SelectOptimizationTest.Main().GetAwaiter().GetResult(); // Not available
                    Console.WriteLine("SelectOptimizationTest not implemented yet.");
                    logWriter.WriteLine("SelectOptimizationTest not implemented yet.");
                    break;
                case "5":
                    Console.WriteLine("Running StructRowBenchmark (Zero-Copy API Performance)...");
                    logWriter.WriteLine("Running StructRowBenchmark...");
                    StructRowBenchmark.RunBenchmarks();
                    CrossEngineBenchmark.RunCrossEngineBenchmarks();
                    Console.WriteLine("\nStructRowBenchmark completed.");
                    logWriter.WriteLine("StructRowBenchmark completed.");
                    break;
                case "0":
                    Console.WriteLine("Exiting...");
                    logWriter.WriteLine("Benchmark session ended.");
                    return;
                default:
                    Console.WriteLine("Invalid option. Exiting...");
                    logWriter.WriteLine("Invalid option selected. Exiting.");
                    return;
            }
        }
    }
}
