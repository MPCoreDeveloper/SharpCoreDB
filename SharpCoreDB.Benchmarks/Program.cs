// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks;

Console.WriteLine("??????????????????????????????????????????????????????????????????????");
Console.WriteLine("?              SharpCoreDB Performance Benchmark Suite               ?");
Console.WriteLine("?                    C# 14 Modernization Edition                     ?");
Console.WriteLine("??????????????????????????????????????????????????????????????????????");
Console.WriteLine();

// Check if running quick test
if (args.Length > 0 && args[0] == "--quick")
{
    Console.WriteLine("Running QUICK performance comparison...");
    Console.WriteLine();
    QuickPerformanceComparison.Run();
    return;
}

Console.WriteLine("Select benchmark mode:");
Console.WriteLine("  1. Quick Comparison (fast, no overhead)");
Console.WriteLine("  2. Full BenchmarkDotNet Suite");
Console.WriteLine("  3. Modernization Benchmark Only");
Console.WriteLine();
Console.Write("Enter choice (1-3): ");

var choice = Console.ReadLine();

switch (choice)
{
    case "1":
        QuickPerformanceComparison.Run();
        break;
        
    case "2":
        Console.WriteLine();
        Console.WriteLine("Running FULL BenchmarkDotNet suite...");
        BenchmarkRunner.Run<ModernizationBenchmark>();
        break;
        
    case "3":
        Console.WriteLine();
        BenchmarkRunner.Run<ModernizationBenchmark>();
        break;
        
    default:
        Console.WriteLine("Invalid choice, running quick comparison...");
        QuickPerformanceComparison.Run();
        break;
}
