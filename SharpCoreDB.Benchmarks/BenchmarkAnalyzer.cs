// <copyright file="BenchmarkAnalyzer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Text.Json;
using System.Text;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Analyzes benchmark results from BenchmarkDotNet output files.
/// Run benchmarks manually, then use this to analyze results.
/// </summary>
public class BenchmarkAnalyzer
{
    private const string ResultsPath = "BenchmarkDotNet.Artifacts/results";
    
    public static void Main(string[] args)
    {
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine("  SharpCoreDB Benchmark Results Analyzer");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine();
        
        if (!Directory.Exists(ResultsPath))
        {
            Console.WriteLine("? No results found!");
            Console.WriteLine($"   Expected directory: {Path.GetFullPath(ResultsPath)}");
            Console.WriteLine();
            Console.WriteLine("Please run benchmarks first:");
            Console.WriteLine("  cd SharpCoreDB.Benchmarks");
            Console.WriteLine("  dotnet run -c Release");
            Console.WriteLine("  Choose option 1 or 2");
            return;
        }

        // Find most recent result files
        var mdFiles = Directory.GetFiles(ResultsPath, "*-report-github.md")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        if (mdFiles.Count == 0)
        {
            Console.WriteLine("? No markdown result files found!");
            Console.WriteLine($"   Searched in: {Path.GetFullPath(ResultsPath)}");
            return;
        }

        Console.WriteLine($"Found {mdFiles.Count} result file(s):");
        Console.WriteLine();

        foreach (var file in mdFiles.Take(5))
        {
            Console.WriteLine($"?? {file.Name}");
            Console.WriteLine($"   Last modified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
            
            AnalyzeMarkdownReport(file.FullName);
            Console.WriteLine();
            Console.WriteLine("???????????????????????????????????????????????????????");
            Console.WriteLine();
        }

        // Also check for JSON files for detailed analysis
        var jsonFiles = Directory.GetFiles(ResultsPath, "*-report-full.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(1)
            .ToList();

        if (jsonFiles.Count > 0)
        {
            Console.WriteLine("?? Detailed JSON Analysis:");
            Console.WriteLine();
            AnalyzeJsonReport(jsonFiles[0].FullName);
        }
    }

    private static void AnalyzeMarkdownReport(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var lines = content.Split('\n');

        Console.WriteLine("?? RESULTS:");
        Console.WriteLine();

        // Find the table
        bool inTable = false;
        var headers = new List<string>();
        var rows = new List<Dictionary<string, string>>();

        foreach (var line in lines)
        {
            if (line.Contains("| Method ") && line.Contains("|"))
            {
                inTable = true;
                headers = line.Split('|')
                    .Select(h => h.Trim())
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .ToList();
                continue;
            }

            if (inTable && line.StartsWith("|---"))
            {
                continue; // Skip separator line
            }

            if (inTable && line.StartsWith("|"))
            {
                var cells = line.Split('|')
                    .Select(c => c.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                if (cells.Count == headers.Count)
                {
                    var row = new Dictionary<string, string>();
                    for (int i = 0; i < headers.Count; i++)
                    {
                        row[headers[i]] = cells[i];
                    }
                    rows.Add(row);
                }
            }

            if (inTable && !line.StartsWith("|"))
            {
                break; // End of table
            }
        }

        if (rows.Count == 0)
        {
            Console.WriteLine("??  No benchmark data found in table");
            return;
        }

        // Analyze results
        Console.WriteLine($"? Found {rows.Count} benchmark results");
        Console.WriteLine();

        // Group by baseline vs optimized (for PageBasedStorageBenchmark)
        var baselineResults = rows.Where(r => r["Method"].Contains("Baseline")).ToList();
        var optimizedResults = rows.Where(r => r["Method"].Contains("Optimized")).ToList();

        if (baselineResults.Any() && optimizedResults.Any())
        {
            Console.WriteLine("?? BASELINE vs OPTIMIZED COMPARISON:");
            Console.WriteLine();
            Console.WriteLine("| Category | Baseline | Optimized | Speedup | Target | Status |");
            Console.WriteLine("|----------|----------|-----------|---------|--------|--------|");

            CompareBenchmarks(baselineResults, optimizedResults, "UPDATE", "Update", "3-5x");
            CompareBenchmarks(baselineResults, optimizedResults, "SELECT", "Select", "5-10x");
            CompareBenchmarks(baselineResults, optimizedResults, "DELETE", "Delete", "3-5x");
            CompareBenchmarks(baselineResults, optimizedResults, "MIXED", "Mixed", "3-4x");
        }
        else
        {
            // Cross-engine comparison
            Console.WriteLine("?? CROSS-ENGINE COMPARISON:");
            Console.WriteLine();

            var groupedByCategory = rows.GroupBy(r => r.ContainsKey("Categories") ? r["Categories"] : "Unknown");
            
            foreach (var group in groupedByCategory)
            {
                Console.WriteLine($"### {group.Key}");
                Console.WriteLine();
                
                var sortedResults = group
                    .Where(r => r.ContainsKey("Mean") && !r["Mean"].Contains("NA"))
                    .OrderBy(r => ParseTime(r["Mean"]))
                    .ToList();

                if (sortedResults.Any())
                {
                    var fastest = sortedResults.First();
                    Console.WriteLine($"?? Fastest: **{fastest["Method"]}** - {fastest["Mean"]}");
                    
                    foreach (var result in sortedResults.Skip(1))
                    {
                        var ratio = ParseTime(result["Mean"]) / ParseTime(fastest["Mean"]);
                        Console.WriteLine($"   {result["Method"]}: {result["Mean"]} ({ratio:F2}x slower)");
                    }
                    Console.WriteLine();
                }
            }
        }

        // Check for issues
        if (content.Contains("Benchmarks with issues:"))
        {
            Console.WriteLine("??  WARNING: Some benchmarks had issues!");
            var issuesStart = content.IndexOf("Benchmarks with issues:");
            var issuesSection = content.Substring(issuesStart).Split('\n').Take(20);
            foreach (var line in issuesSection)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine($"   {line}");
            }
        }
    }

    private static void CompareBenchmarks(
        List<Dictionary<string, string>> baselineResults,
        List<Dictionary<string, string>> optimizedResults,
        string category,
        string displayName,
        string targetSpeedup)
    {
        var baseline = baselineResults.FirstOrDefault(r => r["Method"].Contains(category, StringComparison.OrdinalIgnoreCase));
        var optimized = optimizedResults.FirstOrDefault(r => r["Method"].Contains(category, StringComparison.OrdinalIgnoreCase));

        if (baseline != null && optimized != null && 
            baseline.ContainsKey("Mean") && optimized.ContainsKey("Mean") &&
            !baseline["Mean"].Contains("NA") && !optimized["Mean"].Contains("NA"))
        {
            var baselineTime = ParseTime(baseline["Mean"]);
            var optimizedTime = ParseTime(optimized["Mean"]);
            var speedup = baselineTime / optimizedTime;

            var targetMin = double.Parse(targetSpeedup.Split('-')[0].TrimEnd('x'));
            var status = speedup >= targetMin ? "? HIT" : "?? MISS";

            Console.WriteLine($"| {displayName} | {baseline["Mean"]} | {optimized["Mean"]} | **{speedup:F2}x** | {targetSpeedup} | {status} |");
        }
    }

    private static double ParseTime(string timeStr)
    {
        // Parse formats like "250.0 ms", "1.5 s", "500 us"
        var parts = timeStr.Split(' ');
        if (parts.Length < 2) return 0;

        if (!double.TryParse(parts[0], out var value))
            return 0;

        var unit = parts[1].ToLowerInvariant();
        return unit switch
        {
            "s" => value * 1000,      // seconds to ms
            "ms" => value,            // milliseconds
            "us" => value / 1000,     // microseconds to ms
            "ns" => value / 1000000,  // nanoseconds to ms
            _ => value
        };
    }

    private static void AnalyzeJsonReport(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Benchmarks", out var benchmarks))
            {
                Console.WriteLine($"Total benchmarks: {benchmarks.GetArrayLength()}");
                
                var successful = 0;
                var failed = 0;

                foreach (var benchmark in benchmarks.EnumerateArray())
                {
                    if (benchmark.TryGetProperty("Statistics", out var stats) && 
                        stats.TryGetProperty("Mean", out _))
                    {
                        successful++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                Console.WriteLine($"? Successful: {successful}");
                Console.WriteLine($"? Failed: {failed}");

                if (failed > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Failed benchmarks:");
                    foreach (var benchmark in benchmarks.EnumerateArray())
                    {
                        if (!benchmark.TryGetProperty("Statistics", out var stats) || 
                            !stats.TryGetProperty("Mean", out _))
                        {
                            if (benchmark.TryGetProperty("FullName", out var name))
                            {
                                Console.WriteLine($"  - {name.GetString()}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"??  Could not parse JSON: {ex.Message}");
        }
    }
}
