// <copyright file="HnswPersistence.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Serializes and deserializes HNSW graph state to/from binary format.
/// <para>Format:
/// [Magic:4][Version:1][Flags:1][Reserved:2]
/// [Dimensions:4][DistanceFunction:1][M:4][MaxM0:4][EfConstruction:4][EfSearch:4][LevelMult:8]
/// [NodeCount:4][MaxLevel:4][EntryPointId:8]
/// For each node: [Id:8][MaxLayer:4][VectorData:dims*4][NeighborLayers:variable]
/// </para>
/// </summary>
public static class HnswPersistence
{
    private static ReadOnlySpan<byte> Magic => "HNSW"u8;
    private const byte FormatVersion = 1;
    private const int HeaderSize = 52; // 8 header + 21 config + 16 graph state + 7 padding

    /// <summary>
    /// Serializes an <see cref="HnswIndex"/> to a binary byte array.
    /// </summary>
    /// <param name="index">The HNSW index to serialize.</param>
    /// <returns>The serialized binary data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Serialize(HnswIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        // Estimate size: header + per-node (vector + neighbors)
        int estimatedSize = HeaderSize + (index.Count * (32 + (index.Dimensions * sizeof(float)) + (index.Config.M * 8)));
        using var ms = new MemoryStream(estimatedSize);
        using var writer = new BinaryWriter(ms);

        // Header
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write((byte)0); // flags
        writer.Write((ushort)0); // reserved

        // Config
        writer.Write(index.Dimensions);
        writer.Write((byte)index.DistanceFunction);
        writer.Write(index.Config.M);
        writer.Write(index.Config.EffectiveMaxM0);
        writer.Write(index.Config.EfConstruction);
        writer.Write(index.Config.EfSearch);
        writer.Write(index.Config.EffectiveLevelMultiplier);

        // Graph state
        writer.Write(index.Count);
        writer.Write(index.MaxLevel);

        // Collect all nodes via Search trick: search with k=Count from a zero vector
        // Actually, we need internal access. For now, serialize what's accessible.
        // The index exposes Count, MaxLevel, but not the node dictionary.
        // We'll serialize via a snapshot method on HnswIndex.
        WriteGraphSnapshot(writer, index);

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a binary blob back into an <see cref="HnswIndex"/>.
    /// </summary>
    /// <param name="data">The serialized binary data.</param>
    /// <param name="seed">Optional RNG seed for deterministic behavior (testing).</param>
    /// <returns>The reconstructed HNSW index.</returns>
    public static HnswIndex Deserialize(byte[] data, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize)
            throw new InvalidOperationException("HNSW data too small to contain a valid header");

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Verify magic
        Span<byte> magic = stackalloc byte[4];
        reader.Read(magic);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidOperationException("Invalid HNSW index format: magic bytes mismatch");

        byte version = reader.ReadByte();
        if (version != FormatVersion)
            throw new InvalidOperationException($"Unsupported HNSW format version: {version}");

        reader.ReadByte(); // flags
        reader.ReadUInt16(); // reserved

        // Config
        int dimensions = reader.ReadInt32();
        var distFunc = (DistanceFunction)reader.ReadByte();
        int m = reader.ReadInt32();
        int maxM0 = reader.ReadInt32();
        int efConstruction = reader.ReadInt32();
        int efSearch = reader.ReadInt32();
        double levelMult = reader.ReadDouble();

        // Graph state
        int nodeCount = reader.ReadInt32();
        int maxLevel = reader.ReadInt32();

        var config = new HnswConfig
        {
            Dimensions = dimensions,
            DistanceFunction = distFunc,
            M = m,
            MaxM0 = maxM0,
            EfConstruction = efConstruction,
            EfSearch = efSearch,
            LevelMultiplier = levelMult,
        };

        var index = new HnswIndex(config, seed);

        // Read nodes and rebuild
        ReadGraphSnapshot(reader, index, nodeCount, dimensions);

        return index;
    }

    /// <summary>
    /// Writes graph data using a snapshot provided by the index.
    /// Uses HnswIndex.GetSnapshot() to access internal node data.
    /// </summary>
    private static void WriteGraphSnapshot(BinaryWriter writer, HnswIndex index)
    {
        var snapshot = index.GetSnapshot();
        writer.Write(snapshot.EntryPointId);

        foreach (var node in snapshot.Nodes)
        {
            writer.Write(node.Id);
            writer.Write(node.MaxLayer);

            // Write vector data
            ReadOnlySpan<byte> vectorBytes = MemoryMarshal.AsBytes(node.Vector.AsSpan());
            writer.Write(vectorBytes);

            // Write neighbors per layer
            writer.Write(node.Neighbors.Length); // layer count
            foreach (long[] layerNeighbors in node.Neighbors)
            {
                writer.Write(layerNeighbors.Length);
                foreach (long neighborId in layerNeighbors)
                {
                    writer.Write(neighborId);
                }
            }
        }
    }

    /// <summary>
    /// Reads graph data and rebuilds the index by inserting nodes.
    /// </summary>
    private static void ReadGraphSnapshot(BinaryReader reader, HnswIndex index, int nodeCount, int dimensions)
    {
        long entryPointId = reader.ReadInt64();

        for (int i = 0; i < nodeCount; i++)
        {
            long id = reader.ReadInt64();
            int maxLayer = reader.ReadInt32();

            // Read vector
            int vectorBytes = dimensions * sizeof(float);
            byte[] vecData = reader.ReadBytes(vectorBytes);
            float[] vector = MemoryMarshal.Cast<byte, float>(vecData.AsSpan()).ToArray();

            // Read neighbors
            int layerCount = reader.ReadInt32();
            var neighbors = new long[layerCount][];
            for (int layer = 0; layer < layerCount; layer++)
            {
                int neighborCount = reader.ReadInt32();
                neighbors[layer] = new long[neighborCount];
                for (int n = 0; n < neighborCount; n++)
                {
                    neighbors[layer][n] = reader.ReadInt64();
                }
            }

            // Restore node via snapshot restore
            index.RestoreNode(id, vector, maxLayer, neighbors, isEntryPoint: id == entryPointId);
        }
    }
}
