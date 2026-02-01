// <copyright file="FsmStatistics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

/// <summary>
/// Free Space Manager statistics for monitoring and debugging.
/// C# 14: Record struct with required properties for compile-time safety.
/// </summary>
public readonly record struct FsmStatistics
{
    /// <summary>
    /// Gets the total number of pages in the file.
    /// </summary>
    public required long TotalPages { get; init; }

    /// <summary>
    /// Gets the number of free (unallocated) pages.
    /// </summary>
    public required long FreePages { get; init; }

    /// <summary>
    /// Gets the number of used (allocated) pages.
    /// </summary>
    public required long UsedPages { get; init; }

    /// <summary>
    /// Gets the total free space in bytes.
    /// </summary>
    public required long FreeSpace { get; init; }

    /// <summary>
    /// Gets the size of the largest contiguous free extent (in pages).
    /// </summary>
    public required long LargestExtent { get; init; }

    /// <summary>
    /// Gets the number of free extents tracked.
    /// </summary>
    public required int ExtentCount { get; init; }

    /// <summary>
    /// Gets the fragmentation percentage (0-100).
    /// Higher values indicate more fragmentation.
    /// </summary>
    public required double FragmentationPercent { get; init; }

    /// <summary>
    /// Returns a human-readable summary.
    /// </summary>
    public override string ToString()
    {
        return $"FSM Stats: {UsedPages}/{TotalPages} pages used ({FragmentationPercent:F1}% fragmented), " +
               $"{ExtentCount} extents, largest: {LargestExtent} pages";
    }
}
