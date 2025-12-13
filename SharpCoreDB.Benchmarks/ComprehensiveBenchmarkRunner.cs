// <copyright file="ComprehensiveBenchmarkRunner.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks.Comparative;
using SharpCoreDB.Benchmarks.Infrastructure;
using System.Text;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Comprehensive benchmark runner for comparing SharpCoreDB (with and without encryption)
/// against SQLite and LiteDB across all operations.
/// </summary>
public static class ComprehensiveBenchmarkRunner
{
    public static void Run(string[] args)
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("  SharpCoreDB Comprehensive Performance Benchmark Suite");
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine();
        Console.WriteLine("Comparing:");
        Console.WriteLine("  • SharpCoreDB (WITH encryption)");
        Console.WriteLine("  • SharpCoreDB (WITHOUT encryption)");
        Console.WriteLine("  • SQLite (Memory mode)");
        Console.WriteLine("  • SQLite (File mode)");
        Console.WriteLine("  • LiteDB");
        Console.WriteLine();
        Console.WriteLine("Operations tested:");
        Console.WriteLine("  • INSERT (individual & batch)");
        Console.WriteLine("  • SELECT (point queries, range queries, full scans)");
        Console.WriteLine("  • UPDATE (bulk updates)");
        Console.WriteLine("  • DELETE (bulk deletes)");
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--quick")
        {
            Console.WriteLine("?? QUICK MODE: Running with reduced parameters for fast results...\n");
            RunQuickMode();
        }
        else if (args.Length > 0 && args[0] == "--full")
        {
            Console.WriteLine("?? FULL MODE: Running comprehensive benchmarks (this may take 20-30 minutes)...\n");
            RunFullMode();
        }
        else if (args.Length > 0 && args[0] == "--inserts")
        {
            Console.WriteLine("?? INSERT ONLY: Running insert benchmarks...\n");
            RunInsertBenchmarks();
        }
        else if (args.Length > 0 && args[0] == "--selects")
        {
            Console.WriteLine("?? SELECT ONLY: Running select benchmarks...\n");
            RunSelectBenchmarks();
        }
        else if (args.Length > 0 && args[0] == "--updates")
        {
            Console.WriteLine("?? UPDATE/DELETE ONLY: Running update and delete benchmarks...\n");
            RunUpdateDeleteBenchmarks();
        }
        else
        {
            ShowMenu();
        }
    }

    private static void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("Select benchmark mode:");
            Console.WriteLine("  1. Quick Comparison (fast, reduced parameters)");
            Console.WriteLine("  2. Full Comprehensive Suite (all operations, all sizes)");
            Console.WriteLine("  3. Insert Benchmarks Only");
            Console.WriteLine("  4. Select Benchmarks Only");
            Console.WriteLine("  5. Update/Delete Benchmarks Only");
            Console.WriteLine("  6. All Benchmarks (Sequential)");
            Console.WriteLine("  Q. Quit");
            Console.WriteLine();
            Console.Write("Choice: ");

            var choice = Console.ReadLine()?.Trim().ToUpper();

            switch (choice)
            {
                case "1":
                    RunQuickMode();
                    return;
                case "2":
                    RunFullMode();
                    return;
                case "3":
                    RunInsertBenchmarks();
                    return;
                case "4":
                    RunSelectBenchmarks();
                    return;
                case "5":
                    RunUpdateDeleteBenchmarks();
                    return;
                case "6":
                    RunAllBenchmarks();
                    return;
                case "Q":
                    return;
                default:
                    Console.WriteLine("? Invalid choice. Please try again.\n");
                    break;
            }
        }
    }

    private static void RunQuickMode()
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("  QUICK MODE - Fast Comparison");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");

        var summaries = new List<Summary>();

        // Run with reduced parameters
        Console.WriteLine("?? Running INSERT benchmarks...");
        var insertSummary = BenchmarkRunner.Run<ComparativeInsertBenchmarks>();
        summaries.Add(insertSummary);

        Console.WriteLine("\n?? Running SELECT benchmarks...");
        var selectSummary = BenchmarkRunner.Run<ComparativeSelectBenchmarks>();
        summaries.Add(selectSummary);

        Console.WriteLine("\n?? Running UPDATE/DELETE benchmarks...");
        var updateSummary = BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>();
        summaries.Add(updateSummary);

        Console.WriteLine("\n???????????????????????????????????????????????????????????????");
        Console.WriteLine("  RESULTS SUMMARY");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");

        GenerateComprehensiveReport(summaries);
    }

    private static void RunFullMode()
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("  FULL MODE - Comprehensive Benchmarks");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");

        var summaries = new List<Summary>();

        Console.WriteLine("?? Phase 1/3: INSERT benchmarks (1, 10, 100, 1000 records)...");
        var insertSummary = BenchmarkRunner.Run<ComparativeInsertBenchmarks>();
        summaries.Add(insertSummary);

        Console.WriteLine("\n?? Phase 2/3: SELECT benchmarks (point, range, full scan)...");
        var selectSummary = BenchmarkRunner.Run<ComparativeSelectBenchmarks>();
        summaries.Add(selectSummary);

        Console.WriteLine("\n?? Phase 3/3: UPDATE/DELETE benchmarks...");
        var updateSummary = BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>();
        summaries.Add(updateSummary);

        Console.WriteLine("\n???????????????????????????????????????????????????????????????");
        Console.WriteLine("  COMPREHENSIVE RESULTS");
        Console.WriteLine("???????????????????????????????????????????????????????????????\n");

        GenerateComprehensiveReport(summaries);
        SaveResultsToFile(summaries);
    }

    private static void RunInsertBenchmarks()
    {
        Console.WriteLine("?? Running INSERT benchmarks...\n");
        var summary = BenchmarkRunner.Run<ComparativeInsertBenchmarks>();
        GenerateComprehensiveReport(new List<Summary> { summary });
    }

    private static void RunSelectBenchmarks()
    {
        Console.WriteLine("?? Running SELECT benchmarks...\n");
        var summary = BenchmarkRunner.Run<ComparativeSelectBenchmarks>();
        GenerateComprehensiveReport(new List<Summary> { summary });
    }

    private static void RunUpdateDeleteBenchmarks()
    {
        Console.WriteLine("?? Running UPDATE/DELETE benchmarks...\n");
        var summary = BenchmarkRunner.Run<ComparativeUpdateDeleteBenchmarks>();
        GenerateComprehensiveReport(new List<Summary> { summary });
    }

    private static void RunAllBenchmarks()
    {
        Console.WriteLine("?? Running ALL benchmarks sequentially...\n");
        RunFullMode();
    }

    private static void GenerateComprehensiveReport(List<Summary> summaries)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("???????????????????????????????????????????????????????????????");
        sb.AppendLine("?              PERFORMANCE COMPARISON SUMMARY                 ?");
        sb.AppendLine("???????????????????????????????????????????????????????????????");
        sb.AppendLine();

        // Group results by database type
        var allReports = summaries.SelectMany(s => s.Reports).ToList();
        
        var sharpCoreEncrypted = allReports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("SharpCoreDB (Encrypted)"))
            .ToList();
        
        var sharpCoreNoEncrypt = allReports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("SharpCoreDB (No Encryption)"))
            .ToList();
        
        var sqliteMemory = allReports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("SQLite Memory"))
            .ToList();
        
        var sqliteFile = allReports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("SQLite File"))
            .ToList();
        
        var liteDb = allReports
            .Where(r => r.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo.Contains("LiteDB"))
            .ToList();

        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine("  DATABASE PERFORMANCE AVERAGES");
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine();

        PrintDatabaseStats(sb, "SharpCoreDB (WITH Encryption)", sharpCoreEncrypted);
        PrintDatabaseStats(sb, "SharpCoreDB (NO Encryption)", sharpCoreNoEncrypt);
        PrintDatabaseStats(sb, "SQLite (Memory)", sqliteMemory);
        PrintDatabaseStats(sb, "SQLite (File)", sqliteFile);
        PrintDatabaseStats(sb, "LiteDB", liteDb);

        sb.AppendLine();
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine("  ENCRYPTION IMPACT ANALYSIS");
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine();

        if (sharpCoreEncrypted.Any() && sharpCoreNoEncrypt.Any())
        {
            var encryptedAvg = sharpCoreEncrypted.Average(r => r.ResultStatistics?.Mean ?? 0);
            var noEncryptAvg = sharpCoreNoEncrypt.Average(r => r.ResultStatistics?.Mean ?? 0);
            var overhead = ((encryptedAvg - noEncryptAvg) / noEncryptAvg) * 100;

            sb.AppendLine($"  Average time WITH encryption:    {FormatTime(encryptedAvg)}");
            sb.AppendLine($"  Average time WITHOUT encryption:  {FormatTime(noEncryptAvg)}");
            sb.AppendLine($"  Encryption overhead:              {overhead:F1}%");
            sb.AppendLine();

            if (overhead < 10)
            {
                sb.AppendLine("  ? EXCELLENT: Encryption overhead is minimal (<10%)");
            }
            else if (overhead < 25)
            {
                sb.AppendLine("  ? GOOD: Encryption overhead is acceptable (<25%)");
            }
            else
            {
                sb.AppendLine("  ?? NOTICE: Encryption has significant overhead (>25%)");
            }
        }
        else
        {
            sb.AppendLine("  ?? Insufficient data for encryption comparison");
        }

        sb.AppendLine();
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine("  TOP 5 FASTEST OPERATIONS");
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine();

        var topFive = allReports
            .OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue)
            .Take(5)
            .ToList();

        for (int i = 0; i < topFive.Count; i++)
        {
            var report = topFive[i];
            var medal = i switch
            {
                0 => "??",
                1 => "??",
                2 => "??",
                _ => "  "
            };

            sb.AppendLine($"{medal} #{i + 1}  {report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo}");
            sb.AppendLine($"       {FormatTime(report.ResultStatistics?.Mean ?? 0)}  |  {FormatMemory(report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0)}");
            sb.AppendLine();
        }

        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine("  DETAILED RESULTS");
        sb.AppendLine("?????????????????????????????????????????????????????????????");
        sb.AppendLine();
        sb.AppendLine($"  Results saved to: {Path.GetFullPath("./BenchmarkDotNet.Artifacts/results")}");
        sb.AppendLine();
        sb.AppendLine("  Available formats:");
        sb.AppendLine("    • HTML (interactive reports)");
        sb.AppendLine("    • CSV (Excel-compatible)");
        sb.AppendLine("    • JSON (programmatic access)");
        sb.AppendLine("    • Markdown (GitHub-ready)");
        sb.AppendLine();

        Console.WriteLine(sb.ToString());
    }

    private static void PrintDatabaseStats(StringBuilder sb, string dbName, List<BenchmarkReport> reports)
    {
        if (!reports.Any())
        {
            sb.AppendLine($"  {dbName,-35} No data");
            return;
        }

        var avgTime = reports.Average(r => r.ResultStatistics?.Mean ?? 0);
        var avgMemory = reports.Average(r => r.GcStats.GetBytesAllocatedPerOperation(r.BenchmarkCase));
        var count = reports.Count;

        sb.AppendLine($"  {dbName,-35} {FormatTime(avgTime),15}  |  {FormatMemory((long)avgMemory),15}  ({count} ops)");
    }

    private static string FormatTime(double nanoseconds)
    {
        if (nanoseconds < 1_000)
            return $"{nanoseconds:F2} ns";
        if (nanoseconds < 1_000_000)
            return $"{nanoseconds / 1_000:F2} ?s";
        if (nanoseconds < 1_000_000_000)
            return $"{nanoseconds / 1_000_000:F2} ms";
        return $"{nanoseconds / 1_000_000_000:F2} s";
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes == 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static void SaveResultsToFile(List<Summary> summaries)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"BenchmarkResults_{timestamp}.txt";
            var filepath = Path.Combine("BenchmarkDotNet.Artifacts", "results", filename);

            Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);

            var sb = new StringBuilder();
            sb.AppendLine("???????????????????????????????????????????????????????????????");
            sb.AppendLine("  SharpCoreDB Comprehensive Benchmark Results");
            sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("???????????????????????????????????????????????????????????????");
            sb.AppendLine();

            foreach (var summary in summaries)
            {
                sb.AppendLine($"Benchmark: {summary.Title}");
                sb.AppendLine($"Reports: {summary.Reports.Length}");
                sb.AppendLine();

                foreach (var report in summary.Reports.OrderBy(r => r.ResultStatistics?.Mean))
                {
                    sb.AppendLine($"  {report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo}");
                    sb.AppendLine($"    Mean: {FormatTime(report.ResultStatistics?.Mean ?? 0)}");
                    sb.AppendLine($"    StdDev: {FormatTime(report.ResultStatistics?.StandardDeviation ?? 0)}");
                    sb.AppendLine($"    Memory: {FormatMemory(report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0)}");
                    sb.AppendLine();
                }

                sb.AppendLine("???????????????????????????????????????????????????????????????");
                sb.AppendLine();
            }

            File.WriteAllText(filepath, sb.ToString());

            Console.WriteLine($"?? Results saved to: {Path.GetFullPath(filepath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? Could not save results file: {ex.Message}");
        }
    }
}
