// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;

Console.WriteLine("====================================================");
Console.WriteLine("  SharpCoreDB Storage Engine Benchmarks");
Console.WriteLine("  .NET 10 | C# 14 | BenchmarkDotNet");
Console.WriteLine("====================================================");
Console.WriteLine();

// Check if running in Release mode
#if DEBUG
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("??  WARNING: Running in DEBUG mode!");
Console.WriteLine("   For accurate results, use: dotnet run -c Release");
Console.ResetColor();
Console.WriteLine();
#endif

Console.WriteLine("?? Available Benchmark Suites:");
Console.WriteLine();
Console.WriteLine("  === STORAGE ENGINE BENCHMARKS ===");
Console.WriteLine("  1. PAGE_BASED Before/After  - Validate 3-5x optimization impact (~20 min)");
Console.WriteLine("  2. Cross-Engine Comparison  - SharpCore vs SQLite vs LiteDB (~30 min)");
Console.WriteLine("  7. Run BOTH Storage Benchmarks - ~50 min");
Console.WriteLine();
Console.WriteLine("  0. Exit");
Console.WriteLine();
Console.Write("Select benchmark suite (0, 1, 2, or 7): ");

var choice = Console.ReadLine()?.Trim();

if (choice == "0")
{
    Console.WriteLine("Goodbye!");
    return;
}

// Configure BenchmarkDotNet
var config = ManualConfig
    .Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core90))
    .AddExporter(MarkdownExporter.GitHub)
    .AddExporter(CsvMeasurementsExporter.Default)
    .AddExporter(HtmlExporter.Default);

Console.WriteLine();
Console.WriteLine("??  Starting benchmarks...");
Console.WriteLine("   Results will be saved to: BenchmarkDotNet.Artifacts/results/");
Console.WriteLine();

try
{
    switch (choice)
    {
        case "1":
            Console.WriteLine("?? Running: PAGE_BASED Before/After Optimization Benchmarks");
            Console.WriteLine("   Expected: Validate 3-5x speedup across all operations");
            Console.WriteLine();
            BenchmarkRunner.Run<SharpCoreDB.Benchmarks.PageBasedStorageBenchmark>(config);
            break;

        case "2":
            Console.WriteLine("?? Running: Cross-Engine Comparison Benchmarks");
            Console.WriteLine("   Comparing: AppendOnly, PAGE_BASED, SQLite, LiteDB");
            Console.WriteLine();
            BenchmarkRunner.Run<SharpCoreDB.Benchmarks.StorageEngineComparisonBenchmark>(config);
            break;

        case "7":
            Console.WriteLine("?? Running: BOTH Storage Engine Benchmarks");
            Console.WriteLine("   ??  This will take ~50 minutes!");
            Console.WriteLine();
            Console.Write("Continue? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                BenchmarkRunner.Run<SharpCoreDB.Benchmarks.PageBasedStorageBenchmark>(config);
                BenchmarkRunner.Run<SharpCoreDB.Benchmarks.StorageEngineComparisonBenchmark>(config);
            }
            else
            {
                Console.WriteLine("Cancelled.");
                return;
            }
            break;

        default:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("? Invalid choice!");
            Console.ResetColor();
            return;
    }

    Console.WriteLine();
    Console.WriteLine("====================================================");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("? Benchmarks Complete!");
    Console.ResetColor();
    Console.WriteLine("====================================================");
    Console.WriteLine();
    Console.WriteLine("?? Results saved to:");
    Console.WriteLine("   - BenchmarkDotNet.Artifacts/results/*.md   (Markdown)");
    Console.WriteLine("   - BenchmarkDotNet.Artifacts/results/*.html (HTML)");
    Console.WriteLine("   - BenchmarkDotNet.Artifacts/results/*.csv  (CSV)");
    Console.WriteLine();
    
    if (choice == "1" || choice == "2" || choice == "7")
    {
        Console.WriteLine("?? Compare results against expected values:");
        Console.WriteLine("   docs/benchmarks/STORAGE_BENCHMARK_RESULTS.md");
        Console.WriteLine();
        
        if (choice == "1")
        {
            Console.WriteLine("? Expected Results - PAGE_BASED Before/After:");
            Console.WriteLine("   - INSERT 100K:  850ms ? 250ms  (3.4x speedup)");
            Console.WriteLine("   - UPDATE 50K:   620ms ? 140ms  (4.4x speedup)");
            Console.WriteLine("   - SELECT scan:  180ms ? 28ms   (6.4x speedup)");
            Console.WriteLine("   - DELETE 20K:   480ms ? 110ms  (4.4x speedup)");
            Console.WriteLine("   - Mixed 50K:   1350ms ? 320ms  (4.2x speedup)");
        }
        else if (choice == "2")
        {
            Console.WriteLine("? Expected Results - Cross-Engine:");
            Console.WriteLine("   - SQLite:       Fastest INSERT (42ms vs 250ms)");
            Console.WriteLine("   - PAGE_BASED:   Nearly matches UPDATE (140ms vs 100ms)");
            Console.WriteLine("   - PAGE_BASED:   10x faster SELECT cached (4ms vs 35ms) ??");
            Console.WriteLine("   - LiteDB:       1.5-24x slower than PAGE_BASED");
        }
        
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"? Error running benchmarks: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Stack trace:");
    Console.WriteLine(ex.StackTrace);
}
