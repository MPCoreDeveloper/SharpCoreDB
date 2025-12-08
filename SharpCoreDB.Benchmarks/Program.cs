// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Main entry point for SharpCoreDB benchmarks.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Use the comprehensive Group Commit comparison runner
        GroupCommitComparisonRunner.Run(args);
    }
}
