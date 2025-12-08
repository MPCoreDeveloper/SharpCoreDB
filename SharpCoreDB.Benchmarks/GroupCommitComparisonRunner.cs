// <copyright file="GroupCommitComparisonRunner.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Running;
using BenchmarkDotNet.Reports;
using SharpCoreDB.Benchmarks.Comparative;
using System.Text;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmark runner for Group Commit WAL performance comparison.
/// Runs comprehensive benchmarks against SQLite and LiteDB.
/// </summary>
public static class GroupCommitComparisonRunner
{
    public static void Run(string[] args)
    {
        Console.WriteLine("????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?  SharpCoreDB Group Commit WAL - Performance Comparison Suite    ?");
        Console.WriteLine("?                                                                  ?");
        Console.WriteLine("?  Comparing:                                                      ?");
        Console.WriteLine("?    • SharpCoreDB (Legacy WAL)                                   ?");
        Console.WriteLine("?    • SharpCoreDB (Group Commit FullSync)                        ?");
        Console.WriteLine("?    • SharpCoreDB (Group Commit Async)                           ?");
        Console.WriteLine("?    • SQLite (Memory, WAL mode, No-WAL mode)                     ?");
        Console.WriteLine("?    • LiteDB                                                      ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--quick")
        {
            Console.WriteLine("Running QUICK mode (reduced iterations for fast results)...\n");
            RunQuickBenchmarks();
        }
        else if (args.Length > 0 && args[0] == "--full")
        {
            Console.WriteLine("Running FULL mode (comprehensive benchmarks)...\n");
            RunFullBenchmarks();
        }
        else if (args.Length > 0 && args[0] == "--group-commit")
        {
            Console.WriteLine("Running GROUP COMMIT SPECIFIC benchmarks...\n");
            RunGroupCommitBenchmarks();
        }
        else
        {
            ShowMenu();
        }
    }

    private static void ShowMenu()
    {
        Console.WriteLine("Select benchmark suite:");
        Console.WriteLine("  1. Quick Comparison (fast, fewer iterations)");
        Console.WriteLine("  2. Full Comparison (comprehensive, all scenarios)");
        Console.WriteLine("  3. Group Commit Specific (detailed WAL analysis)");
        Console.WriteLine("  4. Legacy Comparative Benchmarks");
        Console.WriteLine("  5. All Benchmarks");
        Console.WriteLine("  Q. Quit");
        Console.WriteLine();
        Console.Write("Choice: ");

        var choice = Console.ReadLine()?.Trim().ToUpper();

        switch (choice)
        {
            case "1":
                RunQuickBenchmarks();
                break;
            case "2":
                RunFullBenchmarks();
                break;
            case "3":
                RunGroupCommitBenchmarks();
                break;
            case "4":
                RunLegacyBenchmarks();
                break;
            case "5":
                RunAllBenchmarks();
                break;
            case "Q":
                return;
            default:
                Console.WriteLine("Invalid choice. Try again.\n");
                ShowMenu();
                break;
        }
    }

    private static void RunQuickBenchmarks()
    {
        Console.WriteLine("??? QUICK BENCHMARKS ???");
        Console.WriteLine("Running with reduced parameters for fast results...\n");

        var summary = BenchmarkRunner.Run<GroupCommitWALBenchmarks>();
        PrintSummary(summary);
    }

    private static void RunFullBenchmarks()
    {
        Console.WriteLine("??? FULL BENCHMARKS ???");
        Console.WriteLine("This may take 15-30 minutes...\n");

        var summaries = new List<Summary>();

        // Group Commit Benchmarks
        Console.WriteLine("\n? Running Group Commit WAL Benchmarks...");
        summaries.Add(BenchmarkRunner.Run<GroupCommitWALBenchmarks>());

        // Legacy Comparative Benchmarks
        Console.WriteLine("\n? Running Legacy Insert Benchmarks...");
        summaries.Add(BenchmarkRunner.Run<ComparativeInsertBenchmarks>());

        Console.WriteLine("\n? Running Legacy Select Benchmarks...");
        summaries.Add(BenchmarkRunner.Run<ComparativeSelectBenchmarks>());

        Console.WriteLine("\n? Running Legacy Update/Delete Benchmarks...");
        summaries.Add(BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>());

        Console.WriteLine("\n???????????????????????????????????????????????");
        Console.WriteLine("All benchmarks completed!");
        Console.WriteLine($"Results saved to: {Path.GetFullPath("./BenchmarkDotNet.Artifacts")}");
        
        PrintConsolidatedSummary(summaries);
    }

    private static void RunGroupCommitBenchmarks()
    {
        Console.WriteLine("??? GROUP COMMIT WAL BENCHMARKS ???\n");

        var summary = BenchmarkRunner.Run<GroupCommitWALBenchmarks>();
        PrintSummary(summary);
        AnalyzeGroupCommitResults(summary);
    }

    private static void RunLegacyBenchmarks()
    {
        Console.WriteLine("??? LEGACY COMPARATIVE BENCHMARKS ???\n");

        var summaries = new List<Summary>
        {
            BenchmarkRunner.Run<ComparativeInsertBenchmarks>(),
            BenchmarkRunner.Run<ComparativeSelectBenchmarks>(),
            BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>()
        };

        PrintConsolidatedSummary(summaries);
    }

    private static void RunAllBenchmarks()
    {
        Console.WriteLine("??? RUNNING ALL BENCHMARKS ???");
        Console.WriteLine("This will take 30-60 minutes...\n");

        RunFullBenchmarks();
    }

    private static void PrintSummary(Summary summary)
    {
        if (summary == null || !summary.Reports.Any())
        {
            Console.WriteLine("??  No benchmark results available.");
            return;
        }

        Console.WriteLine("\n????????????????????????????????????????????????????????????????");
        Console.WriteLine("?                    BENCHMARK RESULTS                         ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????\n");

        var sb = new StringBuilder();
        sb.AppendLine("Top 5 Fastest Operations:");
        sb.AppendLine("?????????????????????????????????????????????????????????????");

        var topResults = summary.Reports
            .OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue)
            .Take(5)
            .ToList();

        for (int i = 0; i < topResults.Count; i++)
        {
            var report = topResults[i];
            var rank = i + 1;
            var emoji = rank switch
            {
                1 => "??",
                2 => "??",
                3 => "??",
                _ => "  "
            };

            var meanNs = report.ResultStatistics?.Mean ?? 0;
            var meanMs = meanNs / 1_000_000;
            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0;

            sb.AppendLine($"{emoji} #{rank} {report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo}");
            sb.AppendLine($"      Time: {meanMs:F2} ms | Allocated: {FormatBytes(allocated)}");
        }

        Console.WriteLine(sb.ToString());

        Console.WriteLine("\n?? Full results available in:");
        Console.WriteLine($"   {Path.GetFullPath("./BenchmarkDotNet.Artifacts/results")}");
    }

    private static void PrintConsolidatedSummary(List<Summary> summaries)
    {
        Console.WriteLine("\n????????????????????????????????????????????????????????????????");
        Console.WriteLine("?              CONSOLIDATED BENCHMARK RESULTS                  ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????\n");

        var allReports = summaries
            .SelectMany(s => s.Reports)
            .OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue)
            .ToList();

        Console.WriteLine($"Total benchmarks executed: {allReports.Count}\n");

        // Group by database
        var byDatabase = allReports
            .GroupBy(r => GetDatabaseName(r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo))
            .OrderBy(g => g.Average(r => r.ResultStatistics?.Mean ?? double.MaxValue));

        Console.WriteLine("Average Performance by Database:");
        Console.WriteLine("?????????????????????????????????????????????????????????????");

        foreach (var group in byDatabase)
        {
            var avgTime = group.Average(r => r.ResultStatistics?.Mean ?? 0) / 1_000_000;
            var avgAlloc = group.Average(r => r.GcStats.GetBytesAllocatedPerOperation(r.BenchmarkCase));
            
            Console.WriteLine($"  {group.Key,-30} {avgTime,10:F2} ms  |  {FormatBytes((long)avgAlloc),15}");
        }

        Console.WriteLine();
    }

    private static void AnalyzeGroupCommitResults(Summary summary)
    {
        Console.WriteLine("\n????????????????????????????????????????????????????????????????");
        Console.WriteLine("?           GROUP COMMIT WAL PERFORMANCE ANALYSIS              ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????\n");

        var sharpCoreLegacy = summary.Reports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("Legacy WAL"))
            .ToList();

        var sharpCoreGroupCommitFullSync = summary.Reports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("GroupCommit FullSync"))
            .ToList();

        var sharpCoreGroupCommitAsync = summary.Reports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("GroupCommit Async"))
            .ToList();

        var sqlite = summary.Reports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("SQLite"))
            .ToList();

        if (sharpCoreLegacy.Any() && sharpCoreGroupCommitFullSync.Any())
        {
            var legacyAvg = sharpCoreLegacy.Average(r => r.ResultStatistics?.Mean ?? 0);
            var groupCommitAvg = sharpCoreGroupCommitFullSync.Average(r => r.ResultStatistics?.Mean ?? 0);
            var improvement = (legacyAvg - groupCommitAvg) / legacyAvg * 100;

            Console.WriteLine("SharpCoreDB: Legacy WAL vs Group Commit FullSync");
            Console.WriteLine($"  Legacy WAL avg:      {legacyAvg / 1_000_000:F2} ms");
            Console.WriteLine($"  Group Commit avg:    {groupCommitAvg / 1_000_000:F2} ms");
            Console.WriteLine($"  Improvement:         {improvement:F1}% faster ??\n");
        }

        if (sharpCoreGroupCommitAsync.Any() && sqlite.Any())
        {
            var asyncAvg = sharpCoreGroupCommitAsync.Average(r => r.ResultStatistics?.Mean ?? 0);
            var sqliteAvg = sqlite.Average(r => r.ResultStatistics?.Mean ?? 0);
            var ratio = sqliteAvg / asyncAvg;

            Console.WriteLine("SharpCoreDB Group Commit Async vs SQLite");
            Console.WriteLine($"  SharpCoreDB Async:   {asyncAvg / 1_000_000:F2} ms");
            Console.WriteLine($"  SQLite avg:          {sqliteAvg / 1_000_000:F2} ms");
            Console.WriteLine($"  Relative:            {ratio:F2}x {(ratio > 1 ? "faster" : "slower")} than SQLite\n");
        }

        // Analyze concurrency scaling
        Console.WriteLine("\nConcurrency Scaling Analysis:");
        Console.WriteLine("?????????????????????????????????????????????????????????????");

        var concurrentResults = summary.Reports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("Concurrent"))
            .GroupBy(r => r.BenchmarkCase.Parameters.Items.FirstOrDefault(p => p.Name == "ConcurrentThreads")?.Value?.ToString() ?? "1")
            .OrderBy(g => int.Parse(g.Key));

        foreach (var threadGroup in concurrentResults)
        {
            var threadCount = threadGroup.Key;
            Console.WriteLine($"\n  {threadCount} Concurrent Threads:");
            
            var fastest = threadGroup.OrderBy(r => r.ResultStatistics?.Mean).FirstOrDefault();
            if (fastest != null)
            {
                Console.WriteLine($"    Fastest: {GetDatabaseName(fastest.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo)}");
                Console.WriteLine($"             {(fastest.ResultStatistics?.Mean ?? 0) / 1_000_000:F2} ms");
            }
        }
    }

    private static string GetDatabaseName(string methodName)
    {
        if (methodName.Contains("SharpCoreDB") && methodName.Contains("Legacy"))
            return "SharpCoreDB (Legacy WAL)";
        if (methodName.Contains("SharpCoreDB") && methodName.Contains("GroupCommit") && methodName.Contains("FullSync"))
            return "SharpCoreDB (GroupCommit FullSync)";
        if (methodName.Contains("SharpCoreDB") && methodName.Contains("GroupCommit") && methodName.Contains("Async"))
            return "SharpCoreDB (GroupCommit Async)";
        if (methodName.Contains("SharpCoreDB"))
            return "SharpCoreDB";
        if (methodName.Contains("SQLite") && methodName.Contains("Memory"))
            return "SQLite (Memory)";
        if (methodName.Contains("SQLite") && methodName.Contains("WAL"))
            return "SQLite (File WAL)";
        if (methodName.Contains("SQLite") && methodName.Contains("NoWAL"))
            return "SQLite (File No-WAL)";
        if (methodName.Contains("SQLite"))
            return "SQLite";
        if (methodName.Contains("LiteDB"))
            return "LiteDB";
        return "Unknown";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
