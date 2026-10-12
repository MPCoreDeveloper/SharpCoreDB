// <copyright file="VectorBinaryCodecTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpCoreDB.DataStructures;
using Xunit;

/// <summary>
/// Unit tests for <see cref="VectorBinaryCodec"/> — the one definition of the <c>VEC\0</c> vector binary
/// format (docs/Vectors/TECHNICAL_SPEC.md §7) and of the coercion rules every row codec and the parameter
/// binder share. The storage-level round trips live in <see cref="VectorColumnStorageTests"/>.
/// </summary>
public sealed class VectorBinaryCodecTests
{
    [Fact]
    public void Serialize_WritesSpecHeader_AndRoundTrips()
    {
        float[] vector = [0.5f, -0.25f, 0f];

        byte[] blob = VectorBinaryCodec.Serialize(vector);

        Assert.Equal("VEC\0"u8.ToArray(), blob[..4]);
        Assert.Equal(VectorBinaryCodec.FormatVersion, blob[4]);
        Assert.Equal(0, blob[5]); // flags
        Assert.Equal(vector.Length, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(8, 4)));
        Assert.Equal(VectorBinaryCodec.EncodedLength(vector), blob.Length);
        Assert.Equal(vector, VectorBinaryCodec.Deserialize(blob));
        Assert.Equal(vector.Length, VectorBinaryCodec.GetDimensions(blob));
    }

    [Fact]
    public void Deserialize_RawFloatBytes_WithoutHeader_StillReads()
    {
        // Externally produced embeddings have no header; that tolerance is kept.
        float[] vector = [1.5f, -2.5f];
        byte[] raw = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

        Assert.False(VectorBinaryCodec.HasHeader(raw));
        Assert.Equal(vector, VectorBinaryCodec.Deserialize(raw));
        Assert.Equal(vector.Length, VectorBinaryCodec.GetDimensions(raw));
    }

    [Fact]
    public void Deserialize_TruncatedBlob_Throws()
    {
        byte[] blob = VectorBinaryCodec.Serialize([1f, 2f, 3f]);

        Assert.Throws<InvalidOperationException>(() => VectorBinaryCodec.Deserialize(blob.AsSpan(0, blob.Length - 4)));
    }

    [Fact]
    public void ReadLengthPrefixed_TruncatedPayload_Throws()
    {
        float[] vector = [1f, 2f];
        var buffer = new byte[VectorBinaryCodec.EncodedLength(vector) + 4];
        int written = VectorBinaryCodec.WriteLengthPrefixed(buffer, vector);
        Assert.Equal(buffer.Length, written);

        Assert.Equal(vector, VectorBinaryCodec.ReadLengthPrefixed(buffer, out int bytesRead));
        Assert.Equal(written, bytesRead);

        Assert.Throws<InvalidOperationException>(() => VectorBinaryCodec.ReadLengthPrefixed(buffer.AsSpan(0, 6), out _));
    }

    [Fact]
    public void TryParse_AcceptsJsonBase64AndHex()
    {
        float[] vector = [0.5f, 0.25f];
        byte[] raw = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

        Assert.True(VectorBinaryCodec.TryParse("[0.5, 0.25]", out var fromJson));
        Assert.Equal(vector, fromJson);

        Assert.True(VectorBinaryCodec.TryParse($"'{Convert.ToBase64String(raw)}'", out var fromBase64));
        Assert.Equal(vector, fromBase64);

        Assert.True(VectorBinaryCodec.TryParse($"0x{Convert.ToHexString(raw)}", out var fromHex));
        Assert.Equal(vector, fromHex);

        Assert.False(VectorBinaryCodec.TryParse("not-a-vector", out _));
        Assert.False(VectorBinaryCodec.TryParse(string.Empty, out _));
    }

    [Fact]
    public void TryToVector_CoercesTheRepresentationsTheBinderAndCodecsAccept()
    {
        float[] expected = [0.5f, -0.25f];

        Assert.True(VectorBinaryCodec.TryToVector(new float[] { 0.5f, -0.25f }, out var fromArray));
        Assert.Equal(expected, fromArray);

        Assert.True(VectorBinaryCodec.TryToVector(new ReadOnlyMemory<float>(expected), out var fromMemory));
        Assert.Equal(expected, fromMemory);

        Assert.True(VectorBinaryCodec.TryToVector(new List<float> { 0.5f, -0.25f }, out var fromList));
        Assert.Equal(expected, fromList);

        Assert.True(VectorBinaryCodec.TryToVector(new double[] { 0.5, -0.25 }, out var fromDoubles));
        Assert.Equal(expected, fromDoubles);

        Assert.True(VectorBinaryCodec.TryToVector(VectorBinaryCodec.Serialize(expected), out var fromBlob));
        Assert.Equal(expected, fromBlob);

        Assert.False(VectorBinaryCodec.TryToVector(Guid.NewGuid(), out _));
        Assert.False(VectorBinaryCodec.TryToVector(null, out _));
    }

    [Fact]
    public void ToVector_UnsupportedType_ThrowsWithAcceptedForms()
    {
        var error = Assert.Throws<InvalidOperationException>(() => VectorBinaryCodec.ToVector(Guid.NewGuid()));
        Assert.Contains("VECTOR", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("float[]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_ThenTryParse_RoundTrips()
    {
        float[] vector = [0.5f, 0.25f, 0.125f];

        string json = VectorBinaryCodec.ToJson(vector);

        Assert.True(VectorBinaryCodec.TryParse(json, out var parsed));
        Assert.Equal(vector, parsed);
    }
}
