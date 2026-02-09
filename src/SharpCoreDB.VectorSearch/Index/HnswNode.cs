// <copyright file="HnswNode.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Represents a single node in the HNSW graph.
/// Each node stores its vector, layer assignment, and neighbor connections per layer.
/// Thread safety: neighbor arrays are replaced atomically (immutable swap pattern)
/// so readers never see partially-updated lists.
/// </summary>
internal sealed class HnswNode
{
    /// <summary>Unique row identifier (maps back to the database row).</summary>
    internal readonly long Id;

    /// <summary>The vector embedding. Owned by this node — never modified after construction.</summary>
    internal readonly float[] Vector;

    /// <summary>The maximum layer this node is present on (0 = bottom layer only).</summary>
    internal readonly int MaxLayer;

    /// <summary>
    /// Neighbor connections per layer.
    /// <c>Neighbors[layer]</c> is an array of neighbor node IDs at that layer.
    /// Arrays are replaced atomically (never mutated in place) for thread safety.
    /// Layer 0 can have up to MaxM0 neighbors; higher layers can have up to M.
    /// </summary>
    internal volatile long[][] Neighbors;

    /// <summary>
    /// Initializes a new HNSW node.
    /// </summary>
    /// <param name="id">Row identifier.</param>
    /// <param name="vector">Vector data (will be copied).</param>
    /// <param name="maxLayer">Maximum layer for this node.</param>
    internal HnswNode(long id, ReadOnlySpan<float> vector, int maxLayer)
    {
        Id = id;
        Vector = vector.ToArray();
        MaxLayer = maxLayer;

        // Initialize empty neighbor arrays for each layer
        Neighbors = new long[maxLayer + 1][];
        for (int i = 0; i <= maxLayer; i++)
        {
            Neighbors[i] = [];
        }
    }

    /// <summary>
    /// Estimates memory usage for this node in bytes.
    /// </summary>
    internal long EstimatedBytes
    {
        get
        {
            long bytes = 40; // object header + fields
            bytes += Vector.Length * sizeof(float); // vector data
            bytes += 24; // Neighbors array overhead
            var neighbors = Neighbors;
            for (int i = 0; i < neighbors.Length; i++)
            {
                bytes += 16 + (neighbors[i].Length * sizeof(long)); // per-layer array
            }

            return bytes;
        }
    }
}
