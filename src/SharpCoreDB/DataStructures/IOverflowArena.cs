// <copyright file="IOverflowArena.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Shared contract for the fixed-width "out-of-line overflow" arena. A fixed-width record stores
/// variable-length column values as a 4-byte arena block offset in its fixed part; the arena owns
/// the payload blocks and (optionally) reuses freed blocks of equal length in place.
/// </summary>
public interface IOverflowArena
{
    /// <summary>Writes a payload and returns the block offset to store in a record's variable slot.</summary>
    long Write(byte[] payload);

    /// <summary>Reads the payload stored at <paramref name="offset"/>, or null when absent.</summary>
    byte[]? Read(long offset);

    /// <summary>Drops the block at <paramref name="offset"/> from the live set (space is reclaimed
    /// by compaction or exact-length reuse).</summary>
    void Free(long offset);
}
