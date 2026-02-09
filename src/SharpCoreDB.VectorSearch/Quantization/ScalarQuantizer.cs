// <copyright file="ScalarQuantizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Scalar quantization: compresses float32 vectors to uint8 (4× memory reduction).
/// Each dimension is linearly mapped to [0, 255] based on calibrated min/max values.
/// Supports asymmetric distance computation (query in float32, target in uint8).
/// </summary>
public sealed class ScalarQuantizer : IQuantizer
{
    private float[]? _min;
    private float[]? _scale; // 255 / (max - min) per dimension
    private float[]? _invScale; // (max - min) / 255 per dimension
    private int _dimensions;

    /// <inheritdoc />
    public QuantizationType Type => QuantizationType.Scalar8;

    /// <inheritdoc />
    public float CompressionRatio => 4.0f;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public bool IsCalibrated => _min is not null;

    /// <inheritdoc />
    public void Calibrate(IReadOnlyList<float[]> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("At least one sample vector is required for calibration");

        _dimensions = samples[0].Length;
        _min = new float[_dimensions];
        var max = new float[_dimensions];
        _scale = new float[_dimensions];
        _invScale = new float[_dimensions];

        // Initialize min/max from first sample
        Array.Copy(samples[0], _min, _dimensions);
        Array.Copy(samples[0], max, _dimensions);

        // Find global min/max per dimension
        for (int s = 1; s < samples.Count; s++)
        {
            var vec = samples[s];
            if (vec.Length != _dimensions)
                throw new ArgumentException($"Sample {s} has {vec.Length} dimensions, expected {_dimensions}");

            for (int d = 0; d < _dimensions; d++)
            {
                if (vec[d] < _min[d]) _min[d] = vec[d];
                if (vec[d] > max[d]) max[d] = vec[d];
            }
        }

        // Compute scale factors per dimension
        for (int d = 0; d < _dimensions; d++)
        {
            float range = max[d] - _min[d];
            if (range < float.Epsilon)
            {
                // Constant dimension — map to 128
                _scale[d] = 0f;
                _invScale[d] = 0f;
            }
            else
            {
                _scale[d] = 255f / range;
                _invScale[d] = range / 255f;
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Quantize(ReadOnlySpan<float> vector)
    {
        EnsureCalibrated();

        if (vector.Length != _dimensions)
            throw new ArgumentException($"Vector has {vector.Length} dimensions, expected {_dimensions}");

        var result = new byte[_dimensions];
        for (int d = 0; d < _dimensions; d++)
        {
            float normalized = (vector[d] - _min![d]) * _scale![d];
            result[d] = (byte)Math.Clamp(normalized, 0f, 255f);
        }

        return result;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public float[] Dequantize(ReadOnlySpan<byte> quantized)
    {
        EnsureCalibrated();

        if (quantized.Length != _dimensions)
            throw new ArgumentException($"Quantized data has {quantized.Length} bytes, expected {_dimensions}");

        var result = new float[_dimensions];
        for (int d = 0; d < _dimensions; d++)
        {
            result[d] = _min![d] + (quantized[d] * _invScale![d]);
        }

        return result;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public float DistanceQuantized(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget, DistanceFunction distanceFunction)
    {
        EnsureCalibrated();

        // Asymmetric computation: dequantize target on-the-fly, keep query in float32
        // More accurate than quantizing both sides
        return distanceFunction switch
        {
            DistanceFunction.Cosine => AsymmetricCosineDistance(query, quantizedTarget),
            DistanceFunction.Euclidean => AsymmetricEuclideanDistance(query, quantizedTarget),
            DistanceFunction.DotProduct => -AsymmetricDotProduct(query, quantizedTarget),
            _ => throw new ArgumentException($"Unknown distance function: {distanceFunction}"),
        };
    }

    /// <inheritdoc />
    public byte[] SerializeCalibration()
    {
        EnsureCalibrated();

        // Format: [dims:4][min:dims*4][scale:dims*4][invScale:dims*4]
        int size = 4 + (_dimensions * sizeof(float) * 3);
        var data = new byte[size];
        var span = data.AsSpan();

        BitConverter.TryWriteBytes(span, _dimensions);
        MemoryMarshal.AsBytes(_min.AsSpan()).CopyTo(span[4..]);
        MemoryMarshal.AsBytes(_scale.AsSpan()).CopyTo(span[(4 + (_dimensions * 4))..]);
        MemoryMarshal.AsBytes(_invScale.AsSpan()).CopyTo(span[(4 + (_dimensions * 8))..]);

        return data;
    }

    /// <inheritdoc />
    public void DeserializeCalibration(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new InvalidOperationException("Calibration data too small");

        _dimensions = BitConverter.ToInt32(data);
        int expectedSize = 4 + (_dimensions * sizeof(float) * 3);
        if (data.Length < expectedSize)
            throw new InvalidOperationException($"Calibration data too small: expected {expectedSize} bytes, got {data.Length}");

        _min = new float[_dimensions];
        _scale = new float[_dimensions];
        _invScale = new float[_dimensions];

        MemoryMarshal.Cast<byte, float>(data.Slice(4, _dimensions * 4)).CopyTo(_min);
        MemoryMarshal.Cast<byte, float>(data.Slice(4 + (_dimensions * 4), _dimensions * 4)).CopyTo(_scale);
        MemoryMarshal.Cast<byte, float>(data.Slice(4 + (_dimensions * 8), _dimensions * 4)).CopyTo(_invScale);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float AsymmetricCosineDistance(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget)
    {
        float dot = 0f, normQ = 0f, normT = 0f;

        for (int d = 0; d < _dimensions; d++)
        {
            float t = _min![d] + (quantizedTarget[d] * _invScale![d]);
            dot += query[d] * t;
            normQ += query[d] * query[d];
            normT += t * t;
        }

        float denom = MathF.Sqrt(normQ) * MathF.Sqrt(normT);
        return denom < float.Epsilon ? 1f : 1f - (dot / denom);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float AsymmetricEuclideanDistance(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget)
    {
        float sum = 0f;

        for (int d = 0; d < _dimensions; d++)
        {
            float t = _min![d] + (quantizedTarget[d] * _invScale![d]);
            float diff = query[d] - t;
            sum += diff * diff;
        }

        return MathF.Sqrt(sum);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private float AsymmetricDotProduct(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget)
    {
        float dot = 0f;

        for (int d = 0; d < _dimensions; d++)
        {
            float t = _min![d] + (quantizedTarget[d] * _invScale![d]);
            dot += query[d] * t;
        }

        return dot;
    }

    private void EnsureCalibrated()
    {
        if (!IsCalibrated)
            throw new InvalidOperationException("ScalarQuantizer must be calibrated before use. Call Calibrate() first.");
    }
}
