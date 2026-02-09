// <copyright file="ICustomTypeProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Provides custom data type handling for extension modules (e.g., VECTOR type).
/// Registered via DI — zero overhead when not registered.
/// </summary>
public interface ICustomTypeProvider
{
    /// <summary>
    /// Returns true if this provider handles the given SQL type name (e.g., "VECTOR").
    /// </summary>
    /// <param name="typeName">The uppercase type name from SQL (may include parameters like "VECTOR(1536)").</param>
    /// <returns>True if this provider handles the type.</returns>
    bool CanHandle(string typeName);

    /// <summary>
    /// Parses a SQL type declaration and returns the storage DataType plus dimension metadata.
    /// </summary>
    /// <param name="typeDeclaration">The full type declaration (e.g., "VECTOR(1536)").</param>
    /// <param name="dimensions">Output: the number of dimensions (0 if not applicable).</param>
    /// <returns>The base DataType to use for storage.</returns>
    DataType GetStorageType(string typeDeclaration, out int dimensions);

    /// <summary>
    /// Serializes a typed value to bytes for storage.
    /// </summary>
    /// <param name="value">The value to serialize (e.g., float[]).</param>
    /// <param name="dimensions">The expected number of dimensions.</param>
    /// <returns>The serialized byte array.</returns>
    byte[] Serialize(object value, int dimensions);

    /// <summary>
    /// Deserializes bytes back to the typed value.
    /// </summary>
    /// <param name="data">The raw bytes from storage.</param>
    /// <param name="dimensions">The expected number of dimensions.</param>
    /// <returns>The deserialized value (e.g., float[]).</returns>
    object Deserialize(byte[] data, int dimensions);
}
