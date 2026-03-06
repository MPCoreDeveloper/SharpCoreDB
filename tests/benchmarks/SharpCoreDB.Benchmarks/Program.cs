// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Benchmark harness entry point for SharpCoreDB vs BLite vs Zvec comparison.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("SharpCoreDB Benchmark Suite");
        Console.WriteLine("============================");
        Console.WriteLine();

        // Capture environment
        var environment = CaptureEnvironment();
        Console.WriteLine($"Environment: {environment.Runtime.DotnetVersion} on {environment.OS.Name}");
        Console.WriteLine($"CPU: {environment.Hardware.CPU} ({environment.Hardware.Cores} cores)");
        Console.WriteLine($"Memory: {environment.Hardware.MemoryGB} GB");
        Console.WriteLine();

        // Load configuration
        var config = LoadConfiguration("BenchmarkConfig.json");
        Console.WriteLine($"Configuration: {config.Benchmark.Name}");
        Console.WriteLine();

        // Create results directory
        var resultsDir = CreateResultsDirectory();
        Console.WriteLine($"Results will be saved to: {resultsDir}");
        Console.WriteLine();

        // Run benchmarks
        var results = new BenchmarkResults
        {
            Metadata = new Metadata
            {
                RunDate = DateTime.UtcNow,
                Hardware = environment.Hardware,
                Environment = environment.OS,
                Runtime = environment.Runtime,
            },
            Scenarios = []
        };

        try
        {
            // Execute BLite scenarios
            Console.WriteLine("=== BLite Scenarios ===");
            Console.WriteLine();

            // B1: Basic CRUD
            Console.WriteLine("[Main] Starting B1: Basic CRUD");
            var b1 = new BLite.BliteCrudBenchmark();
            b1.Setup();
            await b1.Run();
            b1.Teardown();
            Console.WriteLine();

            // B2: Batch Insert
            Console.WriteLine("[Main] Starting B2: Batch Insert");
            var b2 = new BLite.BliteBatchInsertBenchmark();
            b2.Setup();
            await b2.Run();
            b2.Teardown();
            Console.WriteLine();

            // B3: Filtered Query
            Console.WriteLine("[Main] Starting B3: Filtered Query");
            var b3 = new BLite.BliteFilteredQueryBenchmark();
            b3.Setup();
            await b3.Run();
            b3.Teardown();
            Console.WriteLine();

            // B4: Mixed Workload
            Console.WriteLine("[Main] Starting B4: Mixed Workload");
            var b4 = new BLite.BliteMixedWorkloadBenchmark();
            b4.Setup();
            await b4.Run();
            b4.Teardown();
            Console.WriteLine();

            // Execute Zvec scenarios (Z1-Z5)
            Console.WriteLine("=== Zvec Scenarios ===");
            Console.WriteLine();

            // Z1: Index Build
            Console.WriteLine("[Main] Starting Z1: Index Build");
            var z1 = new Zvec.ZvecIndexBuildBenchmark();
            z1.Setup();
            await z1.Run();
            z1.Teardown();
            Console.WriteLine();

            // Z2: Top-K Query Latency
            Console.WriteLine("[Main] Starting Z2: Top-K Query Latency");
            var z2 = new Zvec.ZvecTopKLatencyBenchmark();
            z2.Setup();
            await z2.Run();
            z2.Teardown();
            Console.WriteLine();

            // Z3: Throughput Under Load
            Console.WriteLine("[Main] Starting Z3: Throughput Under Load");
            var z3 = new Zvec.ZvecThroughputBenchmark();
            z3.Setup();
            await z3.Run();
            z3.Teardown();
            Console.WriteLine();

            // Z4: Recall vs Latency
            Console.WriteLine("[Main] Starting Z4: Recall vs Latency");
            var z4 = new Zvec.ZvecRecallLatencyBenchmark();
            z4.Setup();
            await z4.Run();
            z4.Teardown();
            Console.WriteLine();

            // Z5: Incremental Insert
            Console.WriteLine("[Main] Starting Z5: Incremental Insert");
            var z5 = new Zvec.ZvecIncrementalInsertBenchmark();
            z5.Setup();
            await z5.Run();
            z5.Teardown();
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during benchmarks: {ex.Message}");
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            return 1;
        }

        // Save results
        SaveResults(results, resultsDir);
        Console.WriteLine("Benchmark run complete. Results saved.");

        return 0;
    }

    static EnvironmentSnapshot CaptureEnvironment()
    {
        return new EnvironmentSnapshot
        {
            Hardware = new HardwareInfo
            {
                CPU = GetCPUInfo(),
                Cores = Environment.ProcessorCount,
                MemoryGB = GetMemoryGB(),
                Storage = "Unknown (set manually)"
            },
            OS = new OSInfo
            {
                Name = Environment.OSVersion.VersionString,
                Build = GetOSBuild()
            },
            Runtime = new RuntimeInfo
            {
                DotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            }
        };
    }

    static string GetCPUInfo()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("wmic")
            {
                Arguments = "cpu get Name",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(info);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 1)
                {
                    return lines[1].Trim();
                }
            }
        }
        catch { }

        return "Unknown";
    }

    static int GetMemoryGB()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("systeminfo")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(info);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var line = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(x => x.Contains("Total Physical Memory"));
                if (line != null)
                {
                    var mb = int.Parse(System.Text.RegularExpressions.Regex.Replace(line, "[^0-9]", ""));
                    return mb / 1024;
                }
            }
        }
        catch { }

        return 0;
    }

    static string GetOSBuild()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("reg")
            {
                Arguments = "query \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\" /v CurrentBuildNumber",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(info);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                var match = System.Text.RegularExpressions.Regex.Match(output, @"(\d+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        catch { }

        return "Unknown";
    }

    static BenchmarkConfig LoadConfiguration(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configPath}");
        }

        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<BenchmarkConfig>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize configuration");
    }

    static string CreateResultsDirectory()
    {
        var baseDir = Path.Combine("results", DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmss"));
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "raw-csv"));
        return baseDir;
    }

    static void SaveResults(BenchmarkResults results, string resultsDir)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(results, options);
        File.WriteAllText(Path.Combine(resultsDir, "raw-data.json"), json);

        // Save environment separately
        var envJson = JsonSerializer.Serialize(results.Metadata, options);
        File.WriteAllText(Path.Combine(resultsDir, "environment.json"), envJson);
    }
}

/// <summary>
/// Benchmark configuration model.
/// </summary>
public class BenchmarkConfig
{
    [JsonPropertyName("benchmark")]
    public BenchmarkInfo Benchmark { get; set; } = new();

    [JsonPropertyName("scenarios")]
    public ScenariosConfig Scenarios { get; set; } = new();

    [JsonPropertyName("environment")]
    public EnvironmentConfig Environment { get; set; } = new();

    [JsonPropertyName("warmup")]
    public WarmupConfig Warmup { get; set; } = new();

    [JsonPropertyName("run")]
    public RunConfig Run { get; set; } = new();

    [JsonPropertyName("output")]
    public OutputConfig Output { get; set; } = new();
}

public class BenchmarkInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "SharpCoreDB Benchmarks";

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

public class ScenariosConfig
{
    [JsonPropertyName("blite")]
    public Dictionary<string, object>? BLite { get; set; }

    [JsonPropertyName("zvec")]
    public Dictionary<string, object>? Zvec { get; set; }
}

public class EnvironmentConfig
{
    [JsonPropertyName("hardware")]
    public HardwareConfig? Hardware { get; set; }

    [JsonPropertyName("runtime")]
    public RuntimeConfig? Runtime { get; set; }
}

public class HardwareConfig
{
    [JsonPropertyName("cpu")]
    public string? CPU { get; set; }

    [JsonPropertyName("cores")]
    public int Cores { get; set; }

    [JsonPropertyName("memory_gb")]
    public int MemoryGB { get; set; }

    [JsonPropertyName("disk")]
    public string? Disk { get; set; }
}

public class RuntimeConfig
{
    [JsonPropertyName("dotnet_version")]
    public string? DotnetVersion { get; set; }

    [JsonPropertyName("os")]
    public string? OS { get; set; }
}

public class WarmupConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = 100;
}

public class RunConfig
{
    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = 1000;

    [JsonPropertyName("threads")]
    public int Threads { get; set; } = 1;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 300;
}

public class OutputConfig
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "json";

    [JsonPropertyName("csv_export")]
    public bool CsvExport { get; set; } = true;

    [JsonPropertyName("percentiles")]
    public int[] Percentiles { get; set; } = [50, 90, 95, 99, 999];
}

public class EnvironmentSnapshot
{
    [JsonPropertyName("hardware")]
    public HardwareInfo Hardware { get; set; } = new();

    [JsonPropertyName("os")]
    public OSInfo OS { get; set; } = new();

    [JsonPropertyName("runtime")]
    public RuntimeInfo Runtime { get; set; } = new();
}

public class HardwareInfo
{
    [JsonPropertyName("cpu")]
    public string CPU { get; set; } = string.Empty;

    [JsonPropertyName("cores")]
    public int Cores { get; set; }

    [JsonPropertyName("memory_gb")]
    public int MemoryGB { get; set; }

    [JsonPropertyName("storage")]
    public string Storage { get; set; } = string.Empty;
}

public class OSInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("build")]
    public string Build { get; set; } = string.Empty;
}

public class RuntimeInfo
{
    [JsonPropertyName("dotnet_version")]
    public string DotnetVersion { get; set; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;
}

public class BenchmarkResults
{
    [JsonPropertyName("metadata")]
    public Metadata Metadata { get; set; } = new();

    [JsonPropertyName("scenarios")]
    public List<ScenarioResult> Scenarios { get; set; } = [];
}

public class Metadata
{
    [JsonPropertyName("run_date")]
    public DateTime RunDate { get; set; }

    [JsonPropertyName("hardware")]
    public HardwareInfo Hardware { get; set; } = new();

    [JsonPropertyName("environment")]
    public OSInfo Environment { get; set; } = new();

    [JsonPropertyName("runtime")]
    public RuntimeInfo Runtime { get; set; } = new();
}

public class ScenarioResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("measurements")]
    public List<Measurement> Measurements { get; set; } = [];

    [JsonPropertyName("summary")]
    public Summary Summary { get; set; } = new();
}

public class Measurement
{
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public class Summary
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("throughput_ops_sec")]
    public double ThroughputOpsSec { get; set; }

    [JsonPropertyName("latency_p50_ms")]
    public double LatencyP50Ms { get; set; }

    [JsonPropertyName("latency_p99_ms")]
    public double LatencyP99Ms { get; set; }

    [JsonPropertyName("memory_mb")]
    public double MemoryMB { get; set; }
}
