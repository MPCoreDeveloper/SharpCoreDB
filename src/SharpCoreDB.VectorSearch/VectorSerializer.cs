// <copyright file="VectorSerializer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;
using SharpCoreDB.DataStructures;

/// <summary>
/// High-performance vector serialization for the similarity-search index.
/// <para>
/// The binary format itself lives in one place — <see cref="VectorBinaryCodec"/> in the core assembly — and
/// every method here delegates to it. Before that, this class was one of eight independent copies of the
/// vector layout, and a vector written by the engine's row codec was unreadable to the index (and the other
/// way round) as soon as either copy changed.
/// </para>
/// </summary>
public static class VectorSerializer
{
    /// <summary>Header size in bytes: 4 (magic) + 1 (version) + 1 (flags) + 2 (reserved) + 4 (dims) = 12.</summary>
    internal const int HeaderSize = VectorBinaryCodec.HeaderSize;

    /// <summary>
    /// Serializes a float vector to the SharpCoreDB vector binary format.
    /// Format: [Magic:4][Version:1][Flags:1][Reserved:2][Dims:4][float32 data].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Serialize(ReadOnlySpan<float> vector) => VectorBinaryCodec.Serialize(vector);

    /// <summary>
    /// Deserializes a SharpCoreDB vector binary blob back to float[]. Data without the magic header is read as
    /// raw float32 bytes (external embeddings), the tolerance this method always had.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float[] Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return VectorBinaryCodec.Deserialize(data);
    }

    /// <summary>
    /// Deserializes raw float bytes (no header) back to float[].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] DeserializeRaw(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return VectorBinaryCodec.DeserializeRaw(data);
    }

    /// <summary>
    /// Zero-copy span view over the vector data portion of a serialized blob.
    /// Only valid while the source byte[] is alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<float> DeserializeSpan(ReadOnlySpan<byte> data)
        => VectorBinaryCodec.DeserializeSpan(data);

    /// <summary>
    /// Parses a JSON float array string (e.g., "[0.1, 0.2, 0.3]") to float[].
    /// </summary>
    public static float[] FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return VectorBinaryCodec.TryParse(json, out var vector)
            ? vector
            : throw new ArgumentException("Invalid vector JSON: expected a float array");
    }

    /// <summary>
    /// Converts a float vector to a JSON array string.
    /// </summary>
    public static string ToJson(ReadOnlySpan<float> vector) => VectorBinaryCodec.ToJson(vector);

    /// <summary>
    /// Gets the number of dimensions stored in a serialized vector blob.
    /// </summary>
    public static int GetDimensions(ReadOnlySpan<byte> data) => VectorBinaryCodec.GetDimensions(data);
}
