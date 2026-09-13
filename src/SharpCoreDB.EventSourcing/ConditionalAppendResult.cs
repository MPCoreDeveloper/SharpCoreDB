// <copyright file="ConditionalAppendResult.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing;

/// <summary>
/// Result of a conditional (expected-version) append.
/// </summary>
/// <param name="Success">Whether the append was written. False means the version check failed and nothing was written.</param>
/// <param name="ExpectedVersion">The stream length the caller expected.</param>
/// <param name="ActualVersion">
/// The stream length observed under the write lock. On success this is the new
/// stream length after the append; on a conflict it is the length that disagreed.
/// </param>
/// <param name="Results">The per-event append results in order; empty on a conflict.</param>
public readonly record struct ConditionalAppendResult(
    bool Success,
    long ExpectedVersion,
    long ActualVersion,
    IReadOnlyList<AppendResult> Results)
{
    /// <summary>
    /// Creates a successful conditional append result.
    /// </summary>
    /// <param name="expectedVersion">The stream length the caller expected.</param>
    /// <param name="actualVersion">The stream length after the append.</param>
    /// <param name="results">The per-event append results in order.</param>
    public static ConditionalAppendResult Ok(long expectedVersion, long actualVersion, IReadOnlyList<AppendResult> results) =>
        new(true, expectedVersion, actualVersion, results);

    /// <summary>
    /// Creates a conflicting conditional append result; nothing was written.
    /// </summary>
    /// <param name="expectedVersion">The stream length the caller expected.</param>
    /// <param name="actualVersion">The stream length that disagreed.</param>
    public static ConditionalAppendResult Conflict(long expectedVersion, long actualVersion) =>
        new(false, expectedVersion, actualVersion, []);
}
