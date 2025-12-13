// <copyright file="ReadmeUpdater.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using System.Text;
using System.Text.RegularExpressions;

namespace SharpCoreDB.Benchmarks.Infrastructure;

/// <summary>
/// Updates the root README.md file with benchmark results.
/// </summary>
public class ReadmeUpdater
{
    private const string BenchmarkSectionMarker = "<!-- BENCHMARK_RESULTS -->";
    private const string BenchmarkSectionEnd = "<!-- /BENCHMARK_RESULTS -->";

    /// <summary>
    /// Updates the README.md file with new benchmark results.
    /// </summary>
    /// <param name="readmePath">Path to README.md</param>
    /// <param name="benchmarkMarkdown">Markdown content to insert</param>
    public void UpdateReadme(string readmePath, string benchmarkMarkdown)
    {
        if (!File.Exists(readmePath))
        {
            Console.WriteLine($"README not found at: {readmePath}");
            Console.WriteLine("Creating new README with benchmark results...");
            File.WriteAllText(readmePath, GenerateNewReadme(benchmarkMarkdown));
            return;
        }

        var content = File.ReadAllText(readmePath);
        
        // Check if markers exist
        if (content.Contains(BenchmarkSectionMarker))
        {
            // Replace existing section
            content = ReplaceExistingSection(content, benchmarkMarkdown);
        }
        else
        {
            // Append new section
            content = AppendNewSection(content, benchmarkMarkdown);
        }

        File.WriteAllText(readmePath, content);
        Console.WriteLine($"? README updated at: {readmePath}");
    }

    private string ReplaceExistingSection(string content, string benchmarkMarkdown)
    {
        var pattern = $@"{Regex.Escape(BenchmarkSectionMarker)}.*?{Regex.Escape(BenchmarkSectionEnd)}";
        var replacement = $"{BenchmarkSectionMarker}\n{benchmarkMarkdown}\n{BenchmarkSectionEnd}";
        
        return Regex.Replace(content, pattern, replacement, RegexOptions.Singleline);
    }

    private string AppendNewSection(string content, string benchmarkMarkdown)
    {
        var sb = new StringBuilder(content);
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(BenchmarkSectionMarker);
        sb.AppendLine(benchmarkMarkdown);
        sb.AppendLine(BenchmarkSectionEnd);
        
        return sb.ToString();
    }

    private string GenerateNewReadme(string benchmarkMarkdown)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# SharpCoreDB");
        sb.AppendLine();
        sb.AppendLine("High-performance embedded database for .NET");
        sb.AppendLine();
        sb.AppendLine(BenchmarkSectionMarker);
        sb.AppendLine(benchmarkMarkdown);
        sb.AppendLine(BenchmarkSectionEnd);
        
        return sb.ToString();
    }

    /// <summary>
    /// Copies chart files to a documentation directory.
    /// </summary>
    public void CopyChartFiles(string artifactsPath, string docsPath)
    {
        if (!Directory.Exists(artifactsPath))
        {
            Console.WriteLine($"Artifacts directory not found: {artifactsPath}");
            return;
        }

        Directory.CreateDirectory(docsPath);

        var chartFiles = Directory.GetFiles(artifactsPath, "*.png", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(artifactsPath, "*.svg", SearchOption.AllDirectories));

        foreach (var chartFile in chartFiles)
        {
            var fileName = Path.GetFileName(chartFile);
            var destPath = Path.Combine(docsPath, fileName);
            
            try
            {
                File.Copy(chartFile, destPath, overwrite: true);
                Console.WriteLine($"Copied chart: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy {fileName}: {ex.Message}");
            }
        }
    }
}
