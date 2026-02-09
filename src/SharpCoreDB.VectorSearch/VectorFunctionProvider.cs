// <copyright file="VectorFunctionProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using SharpCoreDB.Interfaces;

/// <summary>
/// Provides SQL vector functions: vec_distance_cosine, vec_distance_l2, vec_distance_dot,
/// vec_from_float32, vec_to_json, vec_normalize, vec_dimensions.
/// Registered via DI as <see cref="ICustomFunctionProvider"/>.
/// </summary>
public sealed class VectorFunctionProvider : ICustomFunctionProvider
{
    private static readonly string[] SupportedFunctions =
    [
        "VEC_DISTANCE_COSINE",
        "VEC_DISTANCE_L2",
        "VEC_DISTANCE_DOT",
        "VEC_FROM_FLOAT32",
        "VEC_TO_JSON",
        "VEC_NORMALIZE",
        "VEC_DIMENSIONS",
    ];

    private static readonly HashSet<string> FunctionLookup = new(SupportedFunctions, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool CanHandle(string functionName) => FunctionLookup.Contains(functionName);

    /// <inheritdoc />
    public IReadOnlyList<string> GetFunctionNames() => SupportedFunctions;

    /// <inheritdoc />
    public object? Evaluate(string functionName, List<object?> arguments)
    {
        return functionName.ToUpperInvariant() switch
        {
            "VEC_DISTANCE_COSINE" => EvaluateDistance(arguments, DistanceFunction.Cosine),
            "VEC_DISTANCE_L2" => EvaluateDistance(arguments, DistanceFunction.Euclidean),
            "VEC_DISTANCE_DOT" => EvaluateDistance(arguments, DistanceFunction.DotProduct),
            "VEC_FROM_FLOAT32" => EvaluateFromFloat32(arguments),
            "VEC_TO_JSON" => EvaluateToJson(arguments),
            "VEC_NORMALIZE" => EvaluateNormalize(arguments),
            "VEC_DIMENSIONS" => EvaluateDimensions(arguments),
            _ => throw new NotSupportedException($"Vector function '{functionName}' is not supported"),
        };
    }

    /// <summary>
    /// Evaluates a distance function between two vector arguments.
    /// </summary>
    private static object EvaluateDistance(List<object?> arguments, DistanceFunction function)
    {
        if (arguments.Count < 2)
            throw new ArgumentException($"vec_distance requires 2 arguments, got {arguments.Count}");

        ReadOnlySpan<float> a = CoerceToFloatSpan(arguments[0], "first");
        ReadOnlySpan<float> b = CoerceToFloatSpan(arguments[1], "second");

        return DistanceMetrics.Compute(a, b, function);
    }

    /// <summary>
    /// Parses a JSON string to a float[] vector.
    /// </summary>
    private static object EvaluateFromFloat32(List<object?> arguments)
    {
        if (arguments.Count < 1 || arguments[0] is null)
            throw new ArgumentException("vec_from_float32 requires 1 argument");

        return arguments[0] switch
        {
            string json => VectorSerializer.FromJson(json),
            float[] arr => arr,
            byte[] bytes => VectorSerializer.Deserialize(bytes),
            _ => throw new ArgumentException($"vec_from_float32: unsupported argument type {arguments[0]!.GetType().Name}"),
        };
    }

    /// <summary>
    /// Converts a vector to JSON string.
    /// </summary>
    private static object EvaluateToJson(List<object?> arguments)
    {
        if (arguments.Count < 1 || arguments[0] is null)
            throw new ArgumentException("vec_to_json requires 1 argument");

        ReadOnlySpan<float> vec = CoerceToFloatSpan(arguments[0], "argument");
        return VectorSerializer.ToJson(vec);
    }

    /// <summary>
    /// L2-normalizes a vector.
    /// </summary>
    private static object EvaluateNormalize(List<object?> arguments)
    {
        if (arguments.Count < 1 || arguments[0] is null)
            throw new ArgumentException("vec_normalize requires 1 argument");

        ReadOnlySpan<float> vec = CoerceToFloatSpan(arguments[0], "argument");
        return DistanceMetrics.Normalize(vec);
    }

    /// <summary>
    /// Returns the number of dimensions of a vector.
    /// </summary>
    private static object EvaluateDimensions(List<object?> arguments)
    {
        if (arguments.Count < 1 || arguments[0] is null)
            throw new ArgumentException("vec_dimensions requires 1 argument");

        return arguments[0] switch
        {
            float[] arr => arr.Length,
            byte[] bytes => VectorSerializer.GetDimensions(bytes),
            _ => throw new ArgumentException($"vec_dimensions: unsupported argument type {arguments[0]!.GetType().Name}"),
        };
    }

    /// <summary>
    /// Coerces various vector representations to ReadOnlySpan&lt;float&gt;.
    /// Supports: float[], byte[] (serialized), string (JSON).
    /// </summary>
    private static float[] CoerceToFloatSpan(object? value, string paramName)
    {
        return value switch
        {
            float[] arr => arr,
            byte[] bytes => VectorSerializer.Deserialize(bytes),
            string json => VectorSerializer.FromJson(json),
            null => throw new ArgumentException($"Vector {paramName} argument cannot be null"),
            _ => throw new ArgumentException(
                $"Cannot convert {value.GetType().Name} to vector. Expected float[], byte[], or JSON string."),
        };
    }
}
