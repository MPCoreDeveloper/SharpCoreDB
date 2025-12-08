// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
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
