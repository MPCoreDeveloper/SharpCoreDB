// <copyright file="ExpectedVersion.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing;

/// <summary>
/// Sentinel values for the <c>expectedVersion</c> argument of a conditional append.
/// </summary>
/// <remarks>
/// A conditional append compares the caller's expected stream length with the
/// current stream length under the store's write lock and writes only when the
/// two agree. This is the optimistic-concurrency primitive an append-only ledger
/// needs: a correction is a new row, and a writer that lost the race is told so
/// instead of silently forking the stream.
/// </remarks>
public static class ExpectedVersion
{
    /// <summary>
    /// Do not check the stream length; append unconditionally (the legacy behaviour).
    /// </summary>
    public const long Any = -1;

    /// <summary>
    /// Require the stream to be empty. Equivalent to expecting a stream length of zero.
    /// </summary>
    public const long NoStream = 0;
}
