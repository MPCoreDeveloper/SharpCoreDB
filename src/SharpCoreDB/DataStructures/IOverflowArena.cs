// <copyright file="IOverflowArena.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Collections.Generic;

/// <summary>
/// Shared contract for the fixed-width "out-of-line overflow" arena. A fixed-width record stores
/// variable-length column values as a 4-byte arena block offset in its fixed part; the arena owns
/// the payload blocks and (optionally) reuses freed blocks of equal length in place.
/// </summary>
public interface IOverflowArena
{
    /// <summary>Writes a payload and returns the block offset to store in a record's variable slot.</summary>
    long Write(byte[] payload);

    /// <summary>
    /// Writes several payloads and returns their block offsets in the same order.
    /// <para>
    /// Prefer this to a loop of <see cref="Write"/>: the directory-mode arena satisfies it with **one**
    /// storage append for the whole list, while a loop opens the arena file once per value — measured at
    /// 0.4597 ms per value on the multi-row INSERT workload, where three variable-length columns meant
    /// three file opens and ~1.4 ms per row.
    /// </para>
    /// <para>
    /// The default implementation loops <see cref="Write"/>, which is the right answer for
    /// <c>SingleFileOverflowArena</c>: its blocks live in memory and are serialized as a single provider
    /// block at flush, so it has no per-value file open to remove.
    /// </para>
    /// </summary>
    /// <param name="payloads">Payloads to write, in the order the returned offsets must follow.</param>
    /// <returns>One block offset per payload, in the same order.</returns>
    long[] WriteMany(IReadOnlyList<byte[]> payloads)
    {
        var offsets = new long[payloads.Count];
        for (int i = 0; i < payloads.Count; i++)
        {
            offsets[i] = Write(payloads[i]);
        }

        return offsets;
    }

    /// <summary>Reads the payload stored at <paramref name="offset"/>, or null when absent.</summary>
    byte[]? Read(long offset);

    /// <summary>Drops the block at <paramref name="offset"/> from the live set (space is reclaimed
    /// by compaction or exact-length reuse).</summary>
    void Free(long offset);
}
