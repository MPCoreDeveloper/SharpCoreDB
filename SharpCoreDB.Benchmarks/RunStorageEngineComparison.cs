// <copyright file="RunStorageEngineComparison.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using System;

/// <summary>
/// Entry point for running the storage engine comparison test.
/// Usage: dotnet run --project SharpCoreDB.Benchmarks -c Release
/// </summary>
public class RunStorageEngineComparison
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("SharpCoreDB - Storage Engine Proof-of-Concept");
        Console.WriteLine("==============================================");
        Console.WriteLine();
        Console.WriteLine("Running storage engine comparison test...");
        Console.WriteLine();

        try
        {
            StorageEngineComparisonTest.Run();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"? Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
