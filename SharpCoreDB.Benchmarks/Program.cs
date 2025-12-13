// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Running;
using SharpCoreDB.Benchmarks;

Console.WriteLine("???????????????????????????????????????????????????????????????");
Console.WriteLine("  SharpCoreDB Comprehensive Performance Benchmark Suite");
Console.WriteLine("  Database Comparison: SharpCoreDB vs SQLite vs LiteDB");
Console.WriteLine("  With/Without Encryption Analysis");
Console.WriteLine("???????????????????????????????????????????????????????????????");
Console.WriteLine();

// Check for command-line arguments
if (args.Length > 0)
{
    switch (args[0])
    {
        case "--quick":
            Console.WriteLine("?? Running QUICK comparative benchmarks...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--full":
            Console.WriteLine("?? Running FULL comprehensive benchmarks...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--inserts":
            Console.WriteLine("?? Running INSERT benchmarks only...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--selects":
            Console.WriteLine("?? Running SELECT benchmarks only...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--updates":
            Console.WriteLine("?? Running UPDATE/DELETE benchmarks only...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--aggregates":
            Console.WriteLine("?? Running AGGREGATE benchmarks only...\n");
            ComprehensiveBenchmarkRunner.Run(args);
            return;
            
        case "--modernization":
            Console.WriteLine("?? Running C# 14 modernization benchmarks...\n");
            BenchmarkRunner.Run<ModernizationBenchmark>();
            return;
            
        case "--help":
        case "-h":
            ShowHelp();
            return;
    }
}

// Interactive mode
ShowInteractiveMenu();

static void ShowHelp()
{
    Console.WriteLine("SharpCoreDB Benchmark Suite - Command Line Options");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -c Release -- [option]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --quick           Fast comparison (reduced parameters)");
    Console.WriteLine("  --full            Full comprehensive suite (all operations, all sizes)");
    Console.WriteLine("  --inserts         INSERT benchmarks only");
    Console.WriteLine("  --selects         SELECT benchmarks only");
    Console.WriteLine("  --updates         UPDATE/DELETE benchmarks only");
    Console.WriteLine("  --aggregates      AGGREGATE benchmarks only");
    Console.WriteLine("  --modernization   C# 14 modernization benchmarks");
    Console.WriteLine("  --help, -h        Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -c Release -- --quick");
    Console.WriteLine("  dotnet run -c Release -- --full");
    Console.WriteLine("  dotnet run -c Release -- --inserts");
    Console.WriteLine("  dotnet run -c Release -- --aggregates");
    Console.WriteLine();
    Console.WriteLine("Databases compared:");
    Console.WriteLine("  � SharpCoreDB (WITH encryption)");
    Console.WriteLine("  � SharpCoreDB (WITHOUT encryption)");
    Console.WriteLine("  � SQLite (Memory mode)");
    Console.WriteLine("  � SQLite (File mode)");
    Console.WriteLine("  � LiteDB");
    Console.WriteLine();
}

static void ShowInteractiveMenu()
{
    while (true)
    {
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine("  Select Benchmark Mode:");
        Console.WriteLine("???????????????????????????????????????????????????????????????");
        Console.WriteLine();
        Console.WriteLine("  Database Comparison Benchmarks:");
        Console.WriteLine("    1. Quick Comparison (fast, recommended for testing)");
        Console.WriteLine("    2. Full Comprehensive Suite (20-30 minutes)");
        Console.WriteLine("    3. INSERT Benchmarks Only");
        Console.WriteLine("    4. SELECT Benchmarks Only");
        Console.WriteLine("    5. UPDATE/DELETE Benchmarks Only");
        Console.WriteLine("    6. AGGREGATE Benchmarks Only");
        Console.WriteLine();
        Console.WriteLine("  Other Benchmarks:");
        Console.WriteLine("    7. C# 14 Modernization Benchmark");
        Console.WriteLine("    8. Quick Performance Comparison (no BenchmarkDotNet)");
        Console.WriteLine();
        Console.WriteLine("    H. Help (command-line options)");
        Console.WriteLine("    Q. Quit");
        Console.WriteLine();
        Console.Write("Choice: ");

        var choice = Console.ReadLine()?.Trim().ToUpper();

        switch (choice)
        {
            case "1":
                ComprehensiveBenchmarkRunner.Run(new[] { "--quick" });
                return;
                
            case "2":
                Console.WriteLine();
                Console.WriteLine("??  WARNING: Full suite may take 20-30 minutes.");
                Console.Write("Continue? (Y/N): ");
                if (Console.ReadLine()?.Trim().ToUpper() == "Y")
                {
                    ComprehensiveBenchmarkRunner.Run(new[] { "--full" });
                }
                return;
                
            case "3":
                ComprehensiveBenchmarkRunner.Run(new[] { "--inserts" });
                return;
                
            case "4":
                ComprehensiveBenchmarkRunner.Run(new[] { "--selects" });
                return;
                
            case "5":
                ComprehensiveBenchmarkRunner.Run(new[] { "--updates" });
                return;
                
            case "6":
                ComprehensiveBenchmarkRunner.Run(new[] { "--aggregates" });
                return;
                
            case "7":
                BenchmarkRunner.Run<ModernizationBenchmark>();
                return;
                
            case "8":
                QuickPerformanceComparison.Run();
                return;
                
            case "H":
                Console.WriteLine();
                ShowHelp();
                Console.WriteLine();
                continue;
                
            case "Q":
                return;
                
            default:
                Console.WriteLine();
                Console.WriteLine("? Invalid choice. Please try again.");
                Console.WriteLine();
                break;
        }
    }
}
