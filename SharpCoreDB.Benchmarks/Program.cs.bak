// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB.Benchmarks;
using SharpCoreDB.Benchmarks.Infrastructure;

Console.Clear();
Console.WriteLine("????????????????????????????????????????????????????????????????????????");
Console.WriteLine("?              SharpCoreDB Performance Benchmarks                      ?");
Console.WriteLine("????????????????????????????????????????????????????????????????????????");
Console.WriteLine();

// Show menu
while (true)
{
    Console.WriteLine("???????????????????????????????????????????????????????????????????????");
    Console.WriteLine("  Beschikbare Benchmarks");
    Console.WriteLine("???????????????????????????????????????????????????????????????????????");
    Console.WriteLine();
    Console.WriteLine("  ?? FAIR COMPARISON");
    Console.WriteLine("  ??????????????????????????????????????????????????????????????????");
    Console.WriteLine("  1. Fair Comparison (10K Bulk Inserts)");
    Console.WriteLine("     • 3 SharpCore modes vs SQLite vs LiteDB");
    Console.WriteLine("     • 1 / 8 / 16 threads");
    Console.WriteLine("     • Markdown tabel output");
    Console.WriteLine("     • Tijd: ~2 minuten");
    Console.WriteLine();
    Console.WriteLine("  ?? REALISTIC WORKLOADS");
    Console.WriteLine("  ??????????????????????????????????????????????????????????????????");
    Console.WriteLine("  2. Realistic Workload Benchmark");
    Console.WriteLine("     • Bulk inserts (10K single transaction)");
    Console.WriteLine("     • Individual inserts (10K separate transactions)");
    Console.WriteLine("     • Mixed (5K inserts + 5K updates + 1K queries)");
    Console.WriteLine("     • VACUUM/checkpoint timing");
    Console.WriteLine("     • Tijd: ~5 minuten");
    Console.WriteLine();
    Console.WriteLine("  ?? QUICK TEST");
    Console.WriteLine("  ??????????????????????????????????????????????????????????????????");
    Console.WriteLine("  3. Simple Quick Test (10K inserts, single thread)");
    Console.WriteLine("     • Snelle baseline check");
    Console.WriteLine("     • Tijd: ~30 seconden");
    Console.WriteLine();
    Console.WriteLine("  ?? DEBUG");
    Console.WriteLine("  ??????????????????????????????????????????????????????????????????");
    Console.WriteLine("  4. Test SharpCoreDB Database Creation");
    Console.WriteLine("     • Check of SharpCore files aanmaakt");
    Console.WriteLine("     • Diagnose 0B probleem");
    Console.WriteLine();
    Console.WriteLine("  ???????????????????????????????????????????????????????????????????");
    Console.WriteLine("  Q. Afsluiten");
    Console.WriteLine();
    Console.Write("Kies een optie (1-4 of Q): ");

    var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
    Console.WriteLine();

    switch (choice)
    {
        case "1":
            RunFairComparison();
            break;

        case "2":
            RunRealisticWorkload();
            break;

        case "3":
            RunQuickTest();
            break;

        case "4":
            RunDebugTest();
            break;

        case "q":
        case "quit":
        case "exit":
            Console.WriteLine("?? Tot ziens!");
            return;

        default:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("? Ongeldige keuze. Probeer opnieuw.");
            Console.ResetColor();
            Console.WriteLine();
            continue;
    }

    Console.WriteLine();
    Console.WriteLine("Druk op een toets om door te gaan...");
    Console.ReadKey();
    Console.Clear();
}

static void RunFairComparison()
{
    Console.Clear();
    try
    {
        var benchmark = new FairComparisonBenchmark();
        benchmark.Run();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("? Fair Comparison voltooid!");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n? Error: {ex.Message}");
        Console.ResetColor();
    }
}

static void RunRealisticWorkload()
{
    Console.Clear();
    try
    {
        var benchmark = new RealisticWorkloadBenchmark();
        benchmark.Run();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("? Realistic Workload voltooid!");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n? Error: {ex.Message}");
        Console.ResetColor();
    }
}

static void RunQuickTest()
{
    Console.Clear();
    try
    {
        var benchmark = new SimpleFairBenchmark();
        benchmark.Run();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("? Quick Test voltooid!");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n? Error: {ex.Message}");
        Console.ResetColor();
    }
}

static void RunDebugTest()
{
    Console.Clear();
    Console.WriteLine("Testing SharpCoreDB database creation...\n");

    var testPath = Path.Combine(Path.GetTempPath(), "test_sharpcoredb");

    try
    {
        Console.WriteLine($"Creating database at: {testPath}");
        Console.WriteLine($"Is directory: {Directory.Exists(testPath)}");
        
        using var helper = new BenchmarkDatabaseHelper(testPath, "password", enableEncryption: false);
        
        Console.WriteLine("? Database helper created successfully");
        
        helper.CreateUsersTable();
        
        Console.WriteLine("? Table created successfully");
        
        // Insert one record
        helper.InsertUserBenchmark(1, "Test User", "test@example.com", 25, DateTime.Now, true);
        
        Console.WriteLine("? Record inserted successfully");
        
        // Check the testPath as DIRECTORY
        Console.WriteLine($"\n?? Checking directory: {testPath}");
        if (Directory.Exists(testPath))
        {
            Console.WriteLine($"  Directory exists! Contents:");
            foreach (var file in Directory.GetFiles(testPath, "*.*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                Console.WriteLine($"    {Path.GetFileName(file)}: {info.Length:N0} bytes");
            }
        }
        else
        {
            Console.WriteLine($"  Directory does NOT exist");
        }
        
        // Also check for .db and .wal as files
        var dbFile = testPath + ".db";
        var walFile = testPath + ".wal";
        
        Console.WriteLine($"\n?? Checking as file paths:");
        Console.WriteLine($"  DB file ({dbFile}): {(File.Exists(dbFile) ? $"{new FileInfo(dbFile).Length:N0} bytes" : "NOT FOUND")}");
        Console.WriteLine($"  WAL file ({walFile}): {(File.Exists(walFile) ? $"{new FileInfo(walFile).Length:N0} bytes" : "NOT FOUND")}");
        
        Console.WriteLine("\n? Test completed successfully!");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n? ERROR: {ex.Message}");
        Console.WriteLine($"\nType: {ex.GetType().Name}");
        Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"\nInner exception: {ex.InnerException.Message}");
            Console.WriteLine($"Inner stack:\n{ex.InnerException.StackTrace}");
        }
        Console.ResetColor();
    }
}
