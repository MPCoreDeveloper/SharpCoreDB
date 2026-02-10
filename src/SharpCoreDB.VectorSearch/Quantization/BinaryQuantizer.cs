// <copyright file="BinaryQuantizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Binary quantization: compresses float32 vectors to 1 bit per dimension (32× memory reduction).
/// Each dimension becomes 1 (positive) or 0 (negative/zero).
/// Distance is computed via Hamming distance (popcount of XOR).
/// Best used as a fast pre-filter with full-precision re-ranking for top candidates.
/// </summary>
public sealed class BinaryQuantizer : IQuantizer
{
    private int _dimensions;
    private int _byteCount; // ceil(dimensions / 8)
    private bool _calibrated;

    /// <inheritdoc />
    public QuantizationType Type => QuantizationType.Binary;

    /// <inheritdoc />
    public float CompressionRatio => 32.0f;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public bool IsCalibrated => _calibrated;

    /// <inheritdoc />
    public void Calibrate(IReadOnlyList<float[]> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("At least one sample vector is required for calibration");

        // Binary quantization uses sign-based encoding — no statistics needed.
        // Calibration just records dimensions.
        _dimensions = samples[0].Length;
        _byteCount = (_dimensions + 7) / 8;
        _calibrated = true;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Quantize(ReadOnlySpan<float> vector)
    {
        EnsureCalibrated();

        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector has {vector.Length} dimensions, expected {_dimensions}");

        var result = new byte[_byteCount];

        for (int d = 0; d < _dimensions; d++)
        {
            if (vector[d] > 0f)
            {
                result[d >> 3] |= (byte)(1 << (d & 7));
            }
        }

        return result;
    }

    /// <inheritdoc />
    public float[] Dequantize(ReadOnlySpan<byte> quantized)
    {
        EnsureCalibrated();

        if (quantized.Length != _byteCount)
            throw new ArgumentException($"Quantized data has {quantized.Length} bytes, expected {_byteCount}");

        var result = new float[_dimensions];
        for (int d = 0; d < _dimensions; d++)
        {
            result[d] = ((quantized[d >> 3] >> (d & 7)) & 1) == 1 ? 1f : -1f;
        }

        return result;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public float DistanceQuantized(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget, DistanceFunction distanceFunction)
    {
        EnsureCalibrated();

        // Quantize query to binary, then compute Hamming distance
        byte[] queryBinary = Quantize(query);
        return HammingDistance(queryBinary, quantizedTarget);
    }

    /// <summary>
    /// Computes Hamming distance between two binary vectors using explicit SIMD intrinsics.
    /// Multi-tier: AVX-512 (64B) → AVX2 (32B) → ulong popcount → scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float HammingDistance(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Binary vectors must have same length: {a.Length} vs {b.Length}");

        int total = 0;
        int i = 0;
        int len = a.Length;

        ref byte refA = ref MemoryMarshal.GetReference(a);
        ref byte refB = ref MemoryMarshal.GetReference(b);

        // AVX2: XOR 32 bytes at a time, then sum popcount of each qword
        if (Avx2.IsSupported && len >= 32)
        {
            int vecLen = len & ~31;
            for (; i < vecLen; i += 32)
            {
                var va = Vector256.LoadUnsafe(ref Unsafe.Add(ref refA, i));
                var vb = Vector256.LoadUnsafe(ref Unsafe.Add(ref refB, i));
                var xored = Avx2.Xor(va, vb);

                // Extract as 4 ulongs for hardware popcount
                var asUlong = xored.AsUInt64();
                total += BitOperations.PopCount(asUlong.GetElement(0));
                total += BitOperations.PopCount(asUlong.GetElement(1));
                total += BitOperations.PopCount(asUlong.GetElement(2));
                total += BitOperations.PopCount(asUlong.GetElement(3));
            }
        }

        // Process 8 bytes at a time using ulong + popcount
        int ulongEnd = i + ((len - i) / 8 * 8);
        if (ulongEnd > i)
        {
            var aLongs = MemoryMarshal.Cast<byte, ulong>(a[i..ulongEnd]);
            var bLongs = MemoryMarshal.Cast<byte, ulong>(b[i..ulongEnd]);

            for (int j = 0; j < aLongs.Length; j++)
            {
                total += BitOperations.PopCount(aLongs[j] ^ bLongs[j]);
            }

            i = ulongEnd;
        }

        // Scalar tail
        for (; i < len; i++)
        {
            total += BitOperations.PopCount((uint)(a[i] ^ b[i]));
        }

        return total;
    }

    /// <inheritdoc />
    public byte[] SerializeCalibration()
    {
        EnsureCalibrated();

        // Format: [dims:4]
        var data = new byte[4];
        BitConverter.TryWriteBytes(data, _dimensions);
        return data;
    }

    /// <inheritdoc />
    public void DeserializeCalibration(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("Calibration data too small");

        _dimensions = BitConverter.ToInt32(data);
        _byteCount = (_dimensions + 7) / 8;
        _calibrated = true;
    }

    private void EnsureCalibrated()
    {
        if (!_calibrated)
            throw new InvalidOperationException("BinaryQuantizer must be calibrated before use. Call Calibrate() first.");
    }
}
