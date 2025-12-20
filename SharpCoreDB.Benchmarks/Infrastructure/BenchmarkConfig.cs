// <copyright file="BenchmarkConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Centralized configuration for comparative database benchmarks.
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Ensure artifacts are always kept and written, even on failures
        Options = ConfigOptions.KeepBenchmarkFiles | ConfigOptions.JoinSummary | ConfigOptions.DisableOptimizationsValidator;
        ArtifactsPath = Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts");

        // Job configuration
        AddJob(Job.Default
            .WithGcServer(true)
            .WithGcForce(true)
            .WithWarmupCount(3)
            .WithIterationCount(10));

        // Diagnostics - Remove ThreadingDiagnoser for .NET 10 compatibility
        AddDiagnoser(MemoryDiagnoser.Default);

        // Exporters
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        AddExporter(CsvExporter.Default);
        AddExporter(JsonExporter.Full);
        AddExporter(RPlotExporter.Default);

        // Loggers
        AddLogger(ConsoleLogger.Default);

        // Column providers
        AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
    }
}
