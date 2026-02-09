// <copyright file="IQuantizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Interface for vector quantization — compresses float32 vectors to reduce memory usage.
/// Implementations must be calibrated from a sample of vectors before use.
/// </summary>
public interface IQuantizer
{
    /// <summary>Gets the quantization type.</summary>
    QuantizationType Type { get; }

    /// <summary>Gets the memory compression ratio (e.g., 4.0 for Scalar8, 32.0 for Binary).</summary>
    float CompressionRatio { get; }

    /// <summary>Gets the number of dimensions this quantizer was calibrated for.</summary>
    int Dimensions { get; }

    /// <summary>Gets whether this quantizer has been calibrated.</summary>
    bool IsCalibrated { get; }

    /// <summary>
    /// Calibrates the quantizer from a representative sample of vectors.
    /// Must be called before <see cref="Quantize"/> or <see cref="DistanceQuantized"/>.
    /// </summary>
    /// <param name="samples">Representative vectors (e.g., 1000 random vectors from the dataset).</param>
    void Calibrate(IReadOnlyList<float[]> samples);

    /// <summary>
    /// Quantizes a float32 vector to the compressed format.
    /// </summary>
    /// <param name="vector">The input vector (length must equal <see cref="Dimensions"/>).</param>
    /// <returns>The quantized byte representation.</returns>
    byte[] Quantize(ReadOnlySpan<float> vector);

    /// <summary>
    /// Dequantizes a compressed vector back to float32 (lossy reconstruction).
    /// </summary>
    /// <param name="quantized">The quantized byte representation.</param>
    /// <returns>The reconstructed float32 vector.</returns>
    float[] Dequantize(ReadOnlySpan<byte> quantized);

    /// <summary>
    /// Computes asymmetric distance: query in full float32, database vector in quantized form.
    /// More accurate than symmetric quantized distance.
    /// </summary>
    /// <param name="query">Full-precision query vector.</param>
    /// <param name="quantizedTarget">Quantized database vector.</param>
    /// <param name="distanceFunction">The distance function to approximate.</param>
    /// <returns>Approximate distance value.</returns>
    float DistanceQuantized(ReadOnlySpan<float> query, ReadOnlySpan<byte> quantizedTarget, DistanceFunction distanceFunction);

    /// <summary>
    /// Serializes calibration parameters for persistence.
    /// </summary>
    /// <returns>Binary representation of calibration state.</returns>
    byte[] SerializeCalibration();

    /// <summary>
    /// Restores calibration parameters from a previously serialized state.
    /// </summary>
    /// <param name="data">Binary calibration data.</param>
    void DeserializeCalibration(ReadOnlySpan<byte> data);
}
