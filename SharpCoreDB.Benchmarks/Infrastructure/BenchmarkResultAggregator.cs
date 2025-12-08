// <copyright file="BenchmarkResultAggregator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Reports;
using System.Text;

namespace SharpCoreDB.Benchmarks.Infrastructure;

/// <summary>
/// Aggregates benchmark results and generates summary reports.
/// </summary>
public class BenchmarkResultAggregator
{
    private readonly List<Summary> summaries = new();

    public void AddSummary(Summary summary)
    {
        summaries.Add(summary);
    }

    public string GenerateMarkdownSummary()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("## Benchmark Results (Auto-Generated)");
        sb.AppendLine();
        sb.AppendLine($"**Generated**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("### Executive Summary");
        sb.AppendLine();
        
        GenerateExecutiveSummary(sb);
        
        sb.AppendLine();
        sb.AppendLine("### Detailed Results");
        sb.AppendLine();
        
        foreach (var summary in summaries)
        {
            GenerateBenchmarkSection(sb, summary);
        }
        
        sb.AppendLine();
        sb.AppendLine("### Performance Charts");
        sb.AppendLine();
        sb.AppendLine("Charts are generated in `BenchmarkDotNet.Artifacts/results/` directory.");
        sb.AppendLine();
        
        return sb.ToString();
    }

    private void GenerateExecutiveSummary(StringBuilder sb)
    {
        var winners = AnalyzePerformance();
        
        sb.AppendLine("| Operation | Winner | Performance Advantage |");
        sb.AppendLine("|-----------|--------|----------------------|");
        
        foreach (var (operation, winner, advantage) in winners)
        {
            sb.AppendLine($"| {operation} | **{winner}** | {advantage} |");
        }
    }

    private List<(string Operation, string Winner, string Advantage)> AnalyzePerformance()
    {
        var results = new List<(string, string, string)>();
        
        foreach (var summary in summaries)
        {
            var benchmarkName = summary.Title;
            var reports = summary.Reports.OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue).ToList();
            
            if (reports.Count >= 2)
            {
                var fastest = reports[0];
                var second = reports[1];
                
                var fastestName = ExtractEngineName(fastest.BenchmarkCase.Descriptor.WorkloadMethod.Name);
                var fastestMean = fastest.ResultStatistics?.Mean ?? 0;
                var secondMean = second.ResultStatistics?.Mean ?? 1;
                
                var advantage = secondMean > 0 ? $"{secondMean / fastestMean:F2}x faster" : "N/A";
                
                results.Add((benchmarkName, fastestName, advantage));
            }
        }
        
        return results;
    }

    private string ExtractEngineName(string methodName)
    {
        if (methodName.Contains("SharpCoreDB", StringComparison.OrdinalIgnoreCase))
            return "SharpCoreDB";
        if (methodName.Contains("SQLite", StringComparison.OrdinalIgnoreCase))
            return "SQLite";
        if (methodName.Contains("LiteDB", StringComparison.OrdinalIgnoreCase))
            return "LiteDB";
        return "Unknown";
    }

    private void GenerateBenchmarkSection(StringBuilder sb, Summary summary)
    {
        sb.AppendLine($"#### {summary.Title}");
        sb.AppendLine();
        
        // Generate table
        sb.AppendLine("| Method | Mean | Error | StdDev | Allocated |");
        sb.AppendLine("|--------|------|-------|--------|-----------|");
        
        foreach (var report in summary.Reports.OrderBy(r => r.ResultStatistics?.Mean ?? double.MaxValue))
        {
            var methodName = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
            var stats = report.ResultStatistics;
            var memory = report.GcStats;
            
            var mean = stats?.Mean.ToString("N2") ?? "N/A";
            var error = stats?.StandardError.ToString("N2") ?? "N/A";
            var stdDev = stats?.StandardDeviation.ToString("N2") ?? "N/A";
            
            // Fix: GcStats.GetBytesAllocatedPerOperation requires benchmarkCase parameter
            var bytesAllocated = memory.GetBytesAllocatedPerOperation(report.BenchmarkCase);
            var allocated = bytesAllocated > 0 ? $"{bytesAllocated / 1024.0:F2} KB" : "0 B";
            
            sb.AppendLine($"| {methodName} | {mean} ns | {error} ns | {stdDev} ns | {allocated} |");
        }
        
        sb.AppendLine();
    }

    public Dictionary<string, object> GetStatistics()
    {
        var stats = new Dictionary<string, object>();
        
        stats["TotalBenchmarks"] = summaries.Count;
        stats["TotalReports"] = summaries.Sum(s => s.Reports.Count());
        
        return stats;
    }
}
