// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks;

static IConfig CreateQuickConfig()
{
    var job = Job.InProcess
        .WithId("QuickHost")
        .WithWarmupCount(1)     // ? At least 1 warmup for JIT
        .WithIterationCount(3)  // ? 3 iterations for statistical stability
        .WithLaunchCount(1)
        // Use in-process emit (default) to restore original working mode
        .WithToolchain(BenchmarkDotNet.Toolchains.InProcess.Emit.InProcessEmitToolchain.Instance);

    var resultsPath = Path.Combine("BenchmarkDotNet.Artifacts", "results");
    Directory.CreateDirectory(resultsPath);
    
    // ? NEW: Create a timestamped log file for THIS run
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var logFilePath = Path.Combine(resultsPath, $"benchmark_log_{timestamp}.txt");

    var config = ManualConfig.CreateEmpty();
    config.UnionRule = ConfigUnionRule.AlwaysUseLocal;

    return config
        .AddJob(job)
        .AddLogger(ConsoleLogger.Default)
        .AddLogger(new StreamLogger(new StreamWriter(logFilePath) { AutoFlush = true })) // ? ALWAYS log to file!
        .AddColumnProvider(DefaultColumnProviders.Instance)
        .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest))
        .AddDiagnoser(MemoryDiagnoser.Default)
        .AddExporter(MarkdownExporter.GitHub)
        .AddExporter(CsvExporter.Default)
        .AddExporter(HtmlExporter.Default)
        .AddExporter(JsonExporter.Full)
        .AddExporter(RPlotExporter.Default)  // ? Add R plots
        .WithArtifactsPath(resultsPath)
        .WithOptions(ConfigOptions.DisableOptimizationsValidator | ConfigOptions.JoinSummary);
}

var config = CreateQuickConfig();

Console.WriteLine("==============================================");
Console.WriteLine("  SharpCoreDB Benchmarks Menu (Quick Mode)");
Console.WriteLine("==============================================");
Console.WriteLine("Select a benchmark to run:");
Console.WriteLine("  1) Page-based storage before/after (PageBasedStorageBenchmark)");
Console.WriteLine("  2) Cross-engine comparison (StorageEngineComparisonBenchmark)");
Console.WriteLine("  3) UPDATE performance - Priority 1 validation (UpdatePerformanceTest)");
Console.WriteLine("  4) SELECT optimization - Phase-by-phase speedup (SelectOptimizationTest)");
Console.WriteLine("  5) StructRow API - Zero-copy performance (StructRowBenchmark)");
Console.WriteLine("  0) Exit");
Console.WriteLine();
Console.Write("Enter choice: ");
var input = Console.ReadLine()?.Trim();

// Fallback for non-interactive runs: default to option 2 (cross-engine comparison)
if (string.IsNullOrWhiteSpace(input))
{
    // Try to infer a choice from command-line args (e.g., passing "2")
    try
    {
        var cmdArgs = Environment.GetCommandLineArgs();
        var choiceArg = cmdArgs.FirstOrDefault(a => a == "1" || a == "2" || a == "3" || a == "4" || a == "5" || a == "0" || a?.Equals("q", StringComparison.OrdinalIgnoreCase) == true);
        input = string.IsNullOrWhiteSpace(choiceArg) ? "2" : choiceArg; // Default to 2 when no explicit choice
    }
    catch
    {
        input = "2"; // Safe default
    }
}

Summary? summary = null;

// ? GUARANTEED: Log file that ALWAYS gets written
var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var logPath = Path.Combine("BenchmarkDotNet.Artifacts", "results", $"run_{timestamp}.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
var logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };

try
{
    logWriter.WriteLine($"=== Benchmark Run Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    logWriter.WriteLine($"Selected option: {input}");
    logWriter.WriteLine();

    switch (input)
    {
        case "1":
            Console.WriteLine("Running PageBasedStorageBenchmark (Quick)…");
            logWriter.WriteLine("Running PageBasedStorageBenchmark...");
            summary = BenchmarkRunner.Run<PageBasedStorageBenchmark>(config);
            break;
        case "2":
            Console.WriteLine("Running StorageEngineComparisonBenchmark (Quick)…");
            logWriter.WriteLine("Running StorageEngineComparisonBenchmark...");
            summary = BenchmarkRunner.Run<StorageEngineComparisonBenchmark>(config);
            break;
        case "3":
            Console.WriteLine("Running UpdatePerformanceTest (UPDATE Performance Validation)...");
            logWriter.WriteLine("Running UpdatePerformanceTest...");
            UpdatePerformanceTest.Main();
            Console.WriteLine("\nUpdatePerformanceTest completed.");
            logWriter.WriteLine("UpdatePerformanceTest completed.");
            break;
        case "4":
            Console.WriteLine("Running SelectOptimizationTest (SELECT Phase-by-Phase Speedup)...");
            logWriter.WriteLine("Running SelectOptimizationTest...");
            SelectOptimizationTest.Main().GetAwaiter().GetResult();
            Console.WriteLine("\nSelectOptimizationTest completed.");
            logWriter.WriteLine("SelectOptimizationTest completed.");
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
        case "q":
        case "Q":
            Console.WriteLine("Exiting.");
            logWriter.WriteLine("User exited.");
            break;
        default:
            // If invalid input provided, default to option 2 for CI stability
            Console.WriteLine("Invalid choice. Defaulting to option 2 (Cross-engine comparison).\n");
            logWriter.WriteLine($"Invalid choice: {input}. Defaulting to option 2.");
            summary = BenchmarkRunner.Run<StorageEngineComparisonBenchmark>(config);
            break;
    }

    logWriter.WriteLine();
    logWriter.WriteLine($"=== Benchmark Run Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    logWriter.WriteLine($"Summary is null: {summary == null}");
    if (summary != null)
    {
        logWriter.WriteLine($"Total reports: {summary.Reports.Length}");
        logWriter.WriteLine($"Successful reports: {summary.Reports.Count(r => r.Success)}");
    }
}
finally
{
    logWriter?.Dispose();
    Console.WriteLine($"\n? Run log saved to: {logPath}");
}

// ? NEW: Save a simple summary file
if (summary != null)
{
    try
    {
        var summaryPath = Path.Combine("BenchmarkDotNet.Artifacts", "results", $"SUMMARY_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        var summaryText = new System.Text.StringBuilder();
        
        summaryText.AppendLine("==============================================");
        summaryText.AppendLine($"  Benchmark Summary - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        summaryText.AppendLine("==============================================");
        summaryText.AppendLine();
        summaryText.AppendLine($"Total benchmarks: {summary.Reports.Length}");
        summaryText.AppendLine($"Successful: {summary.Reports.Count(r => r.Success)}");
        summaryText.AppendLine($"Failed: {summary.Reports.Count(r => !r.Success)}");
        summaryText.AppendLine();
        
        if (summary.Reports.Any(r => r.Success))
        {
            summaryText.AppendLine("RESULTS:");
            summaryText.AppendLine();
            
            // ? ENHANCED: Add full table output
            summaryText.AppendLine("| Method | Categories | Mean | Error | StdDev | Ratio | RatioSD | Allocated | Alloc Ratio |");
            summaryText.AppendLine("|--------|-----------|------|-------|--------|-------|---------|-----------|-------------|");
            
            foreach (var report in summary.Reports.Where(r => r.Success).OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue))
            {
                var method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
                var category = report.BenchmarkCase.Descriptor.Categories.FirstOrDefault() ?? "N/A";
                var stats = report.ResultStatistics;
                var mean = stats?.Mean ?? 0;
                var error = stats?.StandardError ?? 0;
                var stdDev = stats?.StandardDeviation ?? 0;
                
                var unit = "ns";
                if (mean > 1_000_000_000) { mean /= 1_000_000_000; error /= 1_000_000_000; stdDev /= 1_000_000_000; unit = "s"; }
                else if (mean > 1_000_000) { mean /= 1_000_000; error /= 1_000_000; stdDev /= 1_000_000; unit = "ms"; }
                else if (mean > 1_000) { mean /= 1_000; error /= 1_000; stdDev /= 1_000; unit = "us"; }
                
                summaryText.AppendLine($"| {method,-30} | {category,-10} | {mean,8:F2} {unit} | {error,8:F2} {unit} | {stdDev,8:F2} {unit} | - | - | - | - |");
            }
        }
        
        if (summary.Reports.Any(r => !r.Success))
        {
            summaryText.AppendLine();
            summaryText.AppendLine("FAILED BENCHMARKS:");
            foreach (var report in summary.Reports.Where(r => !r.Success))
            {
                summaryText.AppendLine($"  - {report.BenchmarkCase.Descriptor.WorkloadMethod.Name}");
            }
        }
        
        File.WriteAllText(summaryPath, summaryText.ToString());
        Console.WriteLine();
        Console.WriteLine($"? Summary saved to: {summaryPath}");
        Console.WriteLine($"   File size: {new FileInfo(summaryPath).Length} bytes");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"? ERROR saving summary: {ex.Message}");
        Console.WriteLine($"   Stack trace: {ex.StackTrace}");
    }
}
else
{
    Console.WriteLine("?? WARNING: Summary was NULL - no results to save!");
}
