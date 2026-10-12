// <copyright file="VectorBinaryCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// The one definition of the SharpCoreDB vector binary format (<c>docs/Vectors/TECHNICAL_SPEC.md</c> §7:
/// magic <c>VEC\0</c>, version 1, a flags byte, 2 reserved bytes, a 4-byte little-endian dimension count,
/// then the float32 payload), and the one place a CLR value is coerced to a <see cref="float"/> array for a
/// <see cref="DataType.Vector"/> column.
/// <para>
/// Every row codec — <c>Table.WriteTypedValueToSpan</c>/<c>ReadTypedValueFromSpan</c>, the variable-payload
/// encoder, the batched writers (<c>InsertBatchOptimized</c>, <c>StreamingRowEncoder</c>,
/// <c>BulkInsertValuePipeline</c>, <c>PageBasedDataWriter</c>), the decoders (<c>BinaryRowDecoder</c>,
/// <c>ColumnarDataReader</c>), <c>SharpCoreDB.VectorSearch.VectorSerializer</c> and the prepared-statement
/// binder — routes through this type. The vector case used to be missing from eight separate per-type
/// switches, so a <c>float[]</c> inserted into a <c>VECTOR(N)</c> column fell through the serializer's
/// <c>default</c> branch and was persisted as the literal text <c>"System.Single[]"</c> (while a
/// bracketed literal failed earlier still, in the VALUES tokenizer). One writer, one reader, one coercion
/// rule set; the format cannot drift between the call sites again.
/// </para>
/// </summary>
public static class VectorBinaryCodec
{
    /// <summary>Magic bytes identifying a SharpCoreDB vector binary blob.</summary>
    private static ReadOnlySpan<byte> Magic => "VEC\0"u8;

    /// <summary>Binary format version written into the blob header.</summary>
    public const byte FormatVersion = 1;

    /// <summary>Header size in bytes: magic(4) + version(1) + flags(1) + reserved(2) + dims(4).</summary>
    public const int HeaderSize = 12;

    /// <summary>Largest payload the row codecs accept, matching every other 100 MB guard in them.</summary>
    public const int MaxEncodedBytes = 1024 * 1024 * 100;

    /// <summary>Bytes a serialized vector of <paramref name="dimensions"/> dimensions occupies.</summary>
    /// <param name="dimensions">Number of float32 elements.</param>
    /// <returns>Header plus payload size in bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodedLength(int dimensions) => HeaderSize + (dimensions * sizeof(float));

    /// <summary>Bytes a serialized <paramref name="vector"/> occupies.</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>Header plus payload size in bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EncodedLength(ReadOnlySpan<float> vector) => HeaderSize + (vector.Length * sizeof(float));

    /// <summary>
    /// Serializes a float vector to the vector binary format.
    /// Format: <c>[Magic:4][Version:1][Flags:1][Reserved:2][Dims:4][float32 data]</c>.
    /// </summary>
    /// <param name="vector">The vector to serialize.</param>
    /// <returns>The encoded blob.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Serialize(ReadOnlySpan<float> vector)
    {
        var result = new byte[EncodedLength(vector)];
        _ = WriteTo(result, vector);
        return result;
    }

    /// <summary>Writes the vector binary format into <paramref name="destination"/>.</summary>
    /// <param name="destination">Destination buffer, at least <see cref="EncodedLength(ReadOnlySpan{float})"/> bytes.</param>
    /// <param name="vector">The vector to write.</param>
    /// <returns>Number of bytes written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int WriteTo(Span<byte> destination, ReadOnlySpan<float> vector)
    {
        int total = EncodedLength(vector);
        if (destination.Length < total)
        {
            throw new InvalidOperationException(
                $"Buffer too small to write Vector: need {total} bytes, have {destination.Length}");
        }

        Magic.CopyTo(destination);
        destination[4] = FormatVersion;
        destination[5] = 0; // flags (reserved for quantization)
        destination[6] = 0; // reserved
        destination[7] = 0; // reserved
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], vector.Length);
        MemoryMarshal.AsBytes(vector).CopyTo(destination[HeaderSize..]);

        return total;
    }

    /// <summary>Whether <paramref name="data"/> starts with the vector magic and is long enough to hold a header.</summary>
    /// <param name="data">Candidate blob.</param>
    /// <returns><c>true</c> when the data is in the enveloped vector format.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasHeader(ReadOnlySpan<byte> data)
        => data.Length >= HeaderSize && data[..4].SequenceEqual(Magic);

    /// <summary>
    /// Deserializes a vector blob. Data without the magic bytes is read as raw float32 payload, so externally
    /// produced embeddings stay readable (the same tolerance <c>VectorSerializer</c> always had).
    /// </summary>
    /// <param name="data">The encoded blob, enveloped or raw.</param>
    /// <returns>The vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float[] Deserialize(ReadOnlySpan<byte> data)
    {
        if (!HasHeader(data))
        {
            // Raw float32 payload: a trailing partial element cannot be interpreted, and silently dropping it
            // would shorten a vector — which corrupts similarity search — so it is rejected instead.
            if (data.Length % sizeof(float) != 0)
            {
                throw new InvalidOperationException(
                    $"Vector data corrupted: {data.Length} bytes is not a whole number of float32 elements");
            }

            return DeserializeRaw(data);
        }

        int dimensions = GetDimensions(data);
        int expectedBytes = dimensions * sizeof(float);
        int availableBytes = data.Length - HeaderSize;
        if (dimensions < 0 || expectedBytes > MaxEncodedBytes || availableBytes < expectedBytes)
        {
            throw new InvalidOperationException(
                $"Vector data corrupted: expected {expectedBytes} bytes for {dimensions} dimensions, got {availableBytes}");
        }

        return MemoryMarshal.Cast<byte, float>(data.Slice(HeaderSize, expectedBytes)).ToArray();
    }

    /// <summary>
    /// Reads the payload as raw float32 data, ignoring any header. A trailing partial float32 element is
    /// dropped, which is the tolerance the raw path has always had for externally produced byte payloads.
    /// </summary>
    /// <param name="data">Raw float32 little-endian bytes.</param>
    /// <returns>The vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] DeserializeRaw(ReadOnlySpan<byte> data)
        => MemoryMarshal.Cast<byte, float>(data).ToArray();

    /// <summary>
    /// Zero-copy view over the float payload of a serialized blob. Only valid while the source is alive.
    /// </summary>
    /// <param name="data">The encoded blob, enveloped or raw.</param>
    /// <returns>A span over the payload.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<float> DeserializeSpan(ReadOnlySpan<byte> data)
    {
        if (HasHeader(data))
        {
            int dimensions = GetDimensions(data);
            int payloadBytes = Math.Min(dimensions * sizeof(float), data.Length - HeaderSize);
            return MemoryMarshal.Cast<byte, float>(data.Slice(HeaderSize, payloadBytes));
        }

        return MemoryMarshal.Cast<byte, float>(data);
    }

    /// <summary>Gets the number of dimensions stored in a serialized blob.</summary>
    /// <param name="data">The encoded blob, enveloped or raw.</param>
    /// <returns>The dimension count; for raw payloads it is inferred from the byte count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDimensions(ReadOnlySpan<byte> data)
        => HasHeader(data) ? BinaryPrimitives.ReadInt32LittleEndian(data[8..]) : data.Length / sizeof(float);

    /// <summary>
    /// Writes the length-prefixed form the row codecs store in a variable-length column:
    /// <c>[int32 length][vector blob]</c>. Paired with <see cref="ReadLengthPrefixed(ReadOnlySpan{byte}, out int)"/>,
    /// so no call site can get the framing wrong on its own.
    /// </summary>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="vector">The vector to write.</param>
    /// <returns>Number of bytes written (4 + blob length).</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int WriteLengthPrefixed(Span<byte> destination, ReadOnlySpan<float> vector)
    {
        int payload = EncodedLength(vector);
        if (destination.Length < 4 + payload)
        {
            throw new InvalidOperationException(
                $"Buffer too small for Vector write: need {4 + payload} bytes, have {destination.Length}");
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination, payload);
        return 4 + WriteTo(destination[4..], vector);
    }

    /// <summary>Reads the length-prefixed form written by <see cref="WriteLengthPrefixed"/>.</summary>
    /// <param name="source">Source buffer positioned at the length prefix.</param>
    /// <param name="bytesRead">Output: bytes consumed, including the 4-byte prefix.</param>
    /// <returns>The vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float[] ReadLengthPrefixed(ReadOnlySpan<byte> source, out int bytesRead)
    {
        if (source.Length < 4)
        {
            throw new InvalidOperationException(
                $"Buffer too small for Vector length: need 4 bytes, have {source.Length}");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(source);
        if (length < HeaderSize || length > MaxEncodedBytes)
        {
            throw new InvalidOperationException(
                $"Invalid Vector length: {length} (expected {HeaderSize}-{MaxEncodedBytes})");
        }

        if (source.Length < 4 + length)
        {
            throw new InvalidOperationException(
                $"Buffer too small for Vector data: need {4 + length} bytes, have {source.Length}");
        }

        bytesRead = 4 + length;
        return Deserialize(source.Slice(4, length));
    }

    /// <summary>
    /// Parses a vector from its textual form: a JSON float array (<c>[0.5, 0.25, 0.125]</c>) or a
    /// base64-encoded blob (enveloped or raw float32 bytes). Single or double quotes around the text are
    /// stripped, so a quoted literal from a SQL statement parses the same as a bare one.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="vector">Output: the parsed vector.</param>
    /// <returns><c>true</c> when the text was a recognised vector representation.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out float[]? vector)
    {
        vector = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        while (text.Length >= 2 &&
               ((text.StartsWith('\'') && text.EndsWith('\'')) || (text.StartsWith('"') && text.EndsWith('"'))))
        {
            text = text[1..^1].Trim();
        }

        if (text.Length == 0)
        {
            return false;
        }

        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            try
            {
                var parsed = AotJsonSerializer.Deserialize<float[]>(text);
                if (parsed is null)
                {
                    return false;
                }

                vector = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // 0x-prefixed hex blob: the form ParameterBinder renders a byte[] parameter in, and the form a
        // byte[] blob literal is written as in SQL.
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var hex = text[2..];
                if (hex.Length == 0 || hex.Length % 2 != 0)
                {
                    return false;
                }

                var hexBytes = Convert.FromHexString(hex);
                if (hexBytes.Length == 0 || hexBytes.Length % sizeof(float) != 0)
                {
                    return false;
                }

                vector = Deserialize(hexBytes);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Base64 fallback. The byte count must be a whole number of float32 elements, so a typo cannot
        // silently shorten the vector.
        try
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            {
                return false;
            }

            vector = Deserialize(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Coerces a CLR value to the <see cref="float"/> array a <see cref="DataType.Vector"/> column stores.
    /// Accepted: <c>float[]</c>, <c>Memory&lt;float&gt;</c> / <c>ReadOnlyMemory&lt;float&gt;</c>,
    /// any <c>IEnumerable&lt;float&gt;</c> (e.g. <c>List&lt;float&gt;</c>), <c>double[]</c> /
    /// <c>IEnumerable&lt;double&gt;</c>, <c>byte[]</c> (vector blob or raw float32), and the textual forms
    /// <see cref="TryParse"/> accepts.
    /// </summary>
    /// <param name="value">The value to coerce.</param>
    /// <param name="vector">Output: the coerced vector.</param>
    /// <returns><c>true</c> when the value could be coerced.</returns>
    public static bool TryToVector(object? value, [NotNullWhen(true)] out float[]? vector)
    {
        switch (value)
        {
            case null or DBNull:
                vector = null;
                return false;
            case float[] floats:
                vector = floats;
                return true;
            case Memory<float> memory:
                vector = memory.ToArray();
                return true;
            case ReadOnlyMemory<float> readOnlyMemory:
                vector = readOnlyMemory.ToArray();
                return true;
            case IReadOnlyList<float> list:
                vector = [.. list];
                return true;
            case IEnumerable<float> floatsEnumerable:
                vector = [.. floatsEnumerable];
                return true;
            case double[] doubles:
                vector = Array.ConvertAll(doubles, static d => (float)d);
                return true;
            case IEnumerable<double> doublesEnumerable:
                {
                    var buffer = new List<float>();
                    foreach (var element in doublesEnumerable)
                    {
                        buffer.Add((float)element);
                    }

                    vector = [.. buffer];
                    return true;
                }

            case byte[] bytes:
                vector = Deserialize(bytes);
                return true;
            case string text:
                return TryParse(text, out vector);
            default:
                vector = null;
                return false;
        }
    }

    /// <summary>
    /// Coerces <paramref name="value"/> or throws with the list of accepted representations. Used by the row
    /// codecs, where silently storing <c>value.ToString()</c> is what lost vector data before.
    /// </summary>
    /// <param name="value">The value to coerce.</param>
    /// <param name="columnName">Column name, for the error message.</param>
    /// <returns>The coerced vector.</returns>
    public static float[] ToVector(object? value, string? columnName = null)
    {
        if (TryToVector(value, out var vector))
        {
            return vector;
        }

        var where = columnName is null ? string.Empty : $" for column '{columnName}'";
        throw new InvalidOperationException(
            $"Cannot store {value?.GetType().Name ?? "null"} in a VECTOR column{where}. Accepted: float[], " +
            "Memory<float>/ReadOnlyMemory<float>, IEnumerable<float>, double[], byte[] (VEC\\0 blob or raw " +
            "float32 bytes), a JSON array string like \"[0.5, 0.25]\" or a base64 string.");
    }

    /// <summary>Formats a vector as the JSON array text the SQL literal and parameter paths accept.</summary>
    /// <param name="vector">The vector to format.</param>
    /// <returns>A JSON float array.</returns>
    public static string ToJson(ReadOnlySpan<float> vector) => AotJsonSerializer.Serialize(vector.ToArray());
}
