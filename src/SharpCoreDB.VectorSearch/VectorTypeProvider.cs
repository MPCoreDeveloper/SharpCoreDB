// <copyright file="VectorTypeProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using SharpCoreDB.Interfaces;

/// <summary>
/// Handles VECTOR(N) column type — parsing, serialization, and validation.
/// Registered via DI as <see cref="ICustomTypeProvider"/>.
/// </summary>
public sealed class VectorTypeProvider : ICustomTypeProvider
{
    private readonly VectorSearchOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="VectorTypeProvider"/> class.
    /// </summary>
    public VectorTypeProvider(VectorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public bool CanHandle(string typeName)
    {
        return typeName.StartsWith("VECTOR", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public DataType GetStorageType(string typeDeclaration, out int dimensions)
    {
        dimensions = ParseDimensions(typeDeclaration);

        if (dimensions > _options.MaxDimensions)
        {
            throw new ArgumentException(
                $"Vector dimensions {dimensions} exceeds maximum {_options.MaxDimensions}. " +
                $"Increase VectorSearchOptions.MaxDimensions or use smaller embeddings.");
        }

        return DataType.Vector;
    }

    /// <inheritdoc />
    public byte[] Serialize(object value, int dimensions)
    {
        float[] vector = value switch
        {
            float[] arr => arr,
            byte[] bytes => VectorSerializer.Deserialize(bytes),
            string json => VectorSerializer.FromJson(json),
            _ => throw new ArgumentException(
                $"Cannot serialize {value.GetType().Name} as vector. Expected float[], byte[], or JSON string."),
        };

        if (dimensions > 0 && vector.Length != dimensions)
        {
            throw new ArgumentException(
                $"Vector has {vector.Length} dimensions, column requires {dimensions}");
        }

        return VectorSerializer.Serialize(vector);
    }

    /// <inheritdoc />
    public object Deserialize(byte[] data, int dimensions)
    {
        var vector = VectorSerializer.Deserialize(data);

        if (dimensions > 0 && vector.Length != dimensions)
        {
            throw new InvalidOperationException(
                $"Stored vector has {vector.Length} dimensions, column expects {dimensions}. Data may be corrupted.");
        }

        return vector;
    }

    /// <summary>
    /// Extracts dimension count from a type declaration like "VECTOR(1536)".
    /// Returns 0 if no dimensions specified (dynamic-dimension column).
    /// </summary>
    internal static int ParseDimensions(string typeDeclaration)
    {
        var upper = typeDeclaration.ToUpperInvariant().Trim();

        if (upper == "VECTOR")
            return 0;

        int openParen = upper.IndexOf('(');
        int closeParen = upper.IndexOf(')');

        if (openParen < 0 || closeParen <= openParen)
            return 0;

        var dimStr = upper.AsSpan()[(openParen + 1)..closeParen].Trim();

        if (int.TryParse(dimStr, System.Globalization.CultureInfo.InvariantCulture, out int dims) && dims > 0)
            return dims;

        throw new ArgumentException($"Invalid VECTOR dimensions: '{typeDeclaration}'. Expected VECTOR(N) where N > 0.");
    }
}
