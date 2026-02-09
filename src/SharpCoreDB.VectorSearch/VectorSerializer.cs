// <copyright file="VectorSerializer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// High-performance vector serialization using zero-copy memory reinterpretation.
/// Converts between float[] and byte[] without element-by-element copying.
/// </summary>
public static class VectorSerializer
{
    /// <summary>Magic bytes identifying a SharpCoreDB vector binary blob.</summary>
    private static ReadOnlySpan<byte> Magic => "VEC\0"u8;

    /// <summary>Current binary format version.</summary>
    private const byte FormatVersion = 1;

    /// <summary>Header size in bytes: 4 (magic) + 1 (version) + 1 (flags) + 2 (reserved) + 4 (dims) = 12.</summary>
    internal const int HeaderSize = 12;

    /// <summary>
    /// Serializes a float vector to the SharpCoreDB vector binary format.
    /// Format: [Magic:4][Version:1][Flags:1][Reserved:2][Dims:4][float32 data].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Serialize(ReadOnlySpan<float> vector)
    {
        int dataBytes = vector.Length * sizeof(float);
        var result = new byte[HeaderSize + dataBytes];
        var span = result.AsSpan();

        // Write header
        Magic.CopyTo(span);
        span[4] = FormatVersion;
        span[5] = 0; // flags (reserved for quantization)
        span[6] = 0; // reserved
        span[7] = 0; // reserved
        BitConverter.TryWriteBytes(span[8..], vector.Length);

        // Write float data via zero-copy reinterpret
        MemoryMarshal.AsBytes(vector).CopyTo(span[HeaderSize..]);

        return result;
    }

    /// <summary>
    /// Deserializes a SharpCoreDB vector binary blob back to float[].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float[] Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < HeaderSize)
        {
            // Fallback: assume raw float data without header (backward compat / external data)
            return DeserializeRaw(data);
        }

        var span = data.AsSpan();

        // Check magic bytes
        if (!span[..4].SequenceEqual(Magic))
        {
            // Not our format — treat as raw float bytes
            return DeserializeRaw(data);
        }

        int dims = BitConverter.ToInt32(span[8..12]);
        int expectedBytes = dims * sizeof(float);
        int availableBytes = data.Length - HeaderSize;

        if (availableBytes < expectedBytes)
        {
            throw new InvalidOperationException(
                $"Vector data corrupted: expected {expectedBytes} bytes for {dims} dimensions, got {availableBytes}");
        }

        return MemoryMarshal.Cast<byte, float>(span.Slice(HeaderSize, expectedBytes)).ToArray();
    }

    /// <summary>
    /// Deserializes raw float bytes (no header) back to float[].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] DeserializeRaw(byte[] data)
    {
        return MemoryMarshal.Cast<byte, float>(data.AsSpan()).ToArray();
    }

    /// <summary>
    /// Zero-copy span view over the vector data portion of a serialized blob.
    /// Only valid while the source byte[] is alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<float> DeserializeSpan(ReadOnlySpan<byte> data)
    {
        if (data.Length >= HeaderSize && data[..4].SequenceEqual(Magic))
        {
            int dims = BitConverter.ToInt32(data[8..12]);
            return MemoryMarshal.Cast<byte, float>(data.Slice(HeaderSize, dims * sizeof(float)));
        }

        return MemoryMarshal.Cast<byte, float>(data);
    }

    /// <summary>
    /// Parses a JSON float array string (e.g., "[0.1, 0.2, 0.3]") to float[].
    /// </summary>
    public static float[] FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<float[]>(json)
            ?? throw new ArgumentException("Invalid vector JSON: expected a float array");
    }

    /// <summary>
    /// Converts a float vector to a JSON array string.
    /// </summary>
    public static string ToJson(ReadOnlySpan<float> vector)
    {
        return JsonSerializer.Serialize(vector.ToArray());
    }

    /// <summary>
    /// Gets the number of dimensions stored in a serialized vector blob.
    /// </summary>
    public static int GetDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length >= HeaderSize && data[..4].SequenceEqual(Magic))
        {
            return BitConverter.ToInt32(data[8..12]);
        }

        // Raw format: infer from byte count
        return data.Length / sizeof(float);
    }
}
