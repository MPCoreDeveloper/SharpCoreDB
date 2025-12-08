// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks;
using SharpCoreDB.Benchmarks.Comparative;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Main entry point for comparative database benchmarks.
/// Runs benchmarks comparing SharpCoreDB vs SQLite vs LiteDB,
/// then automatically updates the root README.md with results.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("???????????????????????????????????????????????????????????");
        Console.WriteLine("  SharpCoreDB Comparative Benchmark Suite");
        Console.WriteLine("  SharpCoreDB vs SQLite vs LiteDB");
        Console.WriteLine("???????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Check for test mode
        if (args.Length > 0 && args[0] == "--test")
        {
            Console.WriteLine("Running SIMPLE TEST benchmark to verify setup...");
            Console.WriteLine();
            var testConfig = new BenchmarkConfig();
            var summary = BenchmarkRunner.Run<SimpleBenchmark>(testConfig);
            
            Console.WriteLine();
            Console.WriteLine($"Test benchmark completed with {summary.Reports.Count()} reports");
            if (summary.Reports.Any())
            {
                Console.WriteLine("? BenchmarkDotNet is working correctly!");
                Console.WriteLine("You can now run full benchmarks.");
            }
            else
            {
                Console.WriteLine("? No reports generated - check for errors above");
            }
            return;
        }

        var benchmarkConfig = new BenchmarkConfig();
        var aggregator = new BenchmarkResultAggregator();

        // Check if running specific benchmark
        if (args.Length > 0 && args[0] == "--filter")
        {
            RunFilteredBenchmarks(args, aggregator);
            return;
        }

        // Run all comparative benchmarks
        Console.WriteLine("Running Comparative Benchmarks...");
        Console.WriteLine("(This may take 10-30 minutes depending on your hardware)");
        Console.WriteLine();

        try
        {
            // 1. Insert Benchmarks
            Console.WriteLine("?? Running Insert Benchmarks...");
            Console.WriteLine("   Testing bulk inserts with 1, 10, 100, and 1000 records...");
            var insertSummary = BenchmarkRunner.Run<ComparativeInsertBenchmarks>(benchmarkConfig);
            Console.WriteLine($"   ? Completed with {insertSummary.Reports.Count()} reports");
            aggregator.AddSummary(insertSummary);

            // 2. Select Benchmarks
            Console.WriteLine();
            Console.WriteLine("?? Running Select Benchmarks...");
            Console.WriteLine("   Testing point queries, range queries, and full scans...");
            var selectSummary = BenchmarkRunner.Run<ComparativeSelectBenchmarks>(benchmarkConfig);
            Console.WriteLine($"   ? Completed with {selectSummary.Reports.Count()} reports");
            aggregator.AddSummary(selectSummary);

            // 3. Update/Delete Benchmarks
            Console.WriteLine();
            Console.WriteLine("?? Running Update/Delete Benchmarks...");
            Console.WriteLine("   Testing updates and deletes with 1, 10, and 100 records...");
            var updateDeleteSummary = BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>(benchmarkConfig);
            Console.WriteLine($"   ? Completed with {updateDeleteSummary.Reports.Count()} reports");
            aggregator.AddSummary(updateDeleteSummary);

            // Generate results
            Console.WriteLine();
            Console.WriteLine("???????????????????????????????????????????????????????????");
            Console.WriteLine("  Generating Results and Updating README");
            Console.WriteLine("???????????????????????????????????????????????????????????");
            Console.WriteLine();

            UpdateReadmeWithResults(aggregator);

            Console.WriteLine();
            Console.WriteLine("? All benchmarks completed successfully!");
            Console.WriteLine("? README.md has been updated with results");
            Console.WriteLine();
            Console.WriteLine("Results location: BenchmarkDotNet.Artifacts/results/");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error running benchmarks: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static void RunFilteredBenchmarks(string[] args, BenchmarkResultAggregator aggregator)
    {
        var filter = args.Length > 1 ? args[1] : "*";
        var filterConfig = new BenchmarkConfig();
        
        Console.WriteLine($"Running filtered benchmarks: {filter}");
        Console.WriteLine();

        if (filter.Contains("Insert", StringComparison.OrdinalIgnoreCase))
        {
            var summary = BenchmarkRunner.Run<ComparativeInsertBenchmarks>(filterConfig);
            aggregator.AddSummary(summary);
            Console.WriteLine($"Completed with {summary.Reports.Count()} reports");
        }
        else if (filter.Contains("Select", StringComparison.OrdinalIgnoreCase))
        {
            var summary = BenchmarkRunner.Run<ComparativeSelectBenchmarks>(filterConfig);
            aggregator.AddSummary(summary);
            Console.WriteLine($"Completed with {summary.Reports.Count()} reports");
        }
        else if (filter.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                 filter.Contains("Delete", StringComparison.OrdinalIgnoreCase))
        {
            var summary = BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>(filterConfig);
            aggregator.AddSummary(summary);
            Console.WriteLine($"Completed with {summary.Reports.Count()} reports");
        }
        else
        {
            Console.WriteLine("Available filters: Insert, Select, Update, Delete");
            return;
        }

        // Update README with filtered results
        UpdateReadmeWithResults(aggregator);
    }

    private static void UpdateReadmeWithResults(BenchmarkResultAggregator aggregator)
    {
        try
        {
            // Find the root README.md by going up from the current directory
            var currentDir = Directory.GetCurrentDirectory();
            Console.WriteLine($"Current directory: {currentDir}");
            
            // Navigate up to find the solution root
            var searchDir = new DirectoryInfo(currentDir);
            DirectoryInfo? rootDir = null;
            
            // Go up maximum 6 levels to find .git or .sln file
            for (int i = 0; i < 6 && searchDir != null; i++)
            {
                Console.WriteLine($"Searching: {searchDir.FullName}");
                
                // Check if this directory contains .git or *.sln
                if (Directory.Exists(Path.Combine(searchDir.FullName, ".git")) ||
                    Directory.GetFiles(searchDir.FullName, "*.sln").Length > 0)
                {
                    rootDir = searchDir;
                    Console.WriteLine($"? Found repository root: {rootDir.FullName}");
                    break;
                }
                searchDir = searchDir.Parent;
            }
            
            if (rootDir == null)
            {
                Console.WriteLine("??  Could not find repository root directory");
                Console.WriteLine("Searching from: " + currentDir);
                return;
            }

            var readmePath = Path.Combine(rootDir.FullName, "README.md");
            Console.WriteLine($"Updating README at: {readmePath}");

            // Generate markdown summary
            var markdownSummary = aggregator.GenerateMarkdownSummary();

            // Update README
            var updater = new ReadmeUpdater();
            updater.UpdateReadme(readmePath, markdownSummary);

            // Copy charts to docs directory
            var artifactsPath = Path.Combine(currentDir, "BenchmarkDotNet.Artifacts");
            var docsPath = Path.Combine(rootDir.FullName, "docs", "benchmarks");
            
            if (Directory.Exists(artifactsPath))
            {
                Console.WriteLine("Copying charts to docs directory...");
                updater.CopyChartFiles(artifactsPath, docsPath);
            }

            // Print statistics
            var stats = aggregator.GetStatistics();
            Console.WriteLine();
            Console.WriteLine("Statistics:");
            Console.WriteLine($"  Total Benchmarks: {stats["TotalBenchmarks"]}");
            Console.WriteLine($"  Total Reports: {stats["TotalReports"]}");
            
            if ((int)stats["TotalReports"] == 0)
            {
                Console.WriteLine();
                Console.WriteLine("??  WARNING: No benchmark reports were generated!");
                Console.WriteLine("This might indicate that benchmarks didn't run properly.");
                Console.WriteLine();
                Console.WriteLine("Try running with --test first:");
                Console.WriteLine("  dotnet run -c Release -- --test");
                Console.WriteLine();
                Console.WriteLine("Or check BenchmarkDotNet.Artifacts/logs/ for error details.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"? Successfully generated {stats["TotalReports"]} benchmark reports!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"??  Error updating README: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine();
            Console.WriteLine("Benchmark results are still available in BenchmarkDotNet.Artifacts/");
        }
    }
}
