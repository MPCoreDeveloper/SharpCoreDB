// <copyright file="AotJsonSerializer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// The one place the core assembly builds <see cref="JsonSerializerOptions"/>, so that every call site can
/// resolve a <see cref="JsonTypeInfo{T}"/> instead of relying on the reflection-based default resolver
/// (which is what the AOT/trim analyzers flag as IL2026 / IL3050).
/// <para>
/// Two resolvers, chosen by the runtime and not by a compile-time switch:
/// while reflection is available (<see cref="JsonSerializer.IsReflectionEnabledByDefault"/> is <c>true</c>,
/// i.e. the JIT path every release ships) the framework's own reflection resolver is used and the produced
/// JSON is byte-identical to the parameterless <c>JsonSerializer</c> overloads this type replaced. Where
/// reflection is disabled (Native AOT / trimming) the source-generated <see cref="SharpCoreDBJsonContext"/>
/// is used instead, so the same call site keeps working without reflection.
/// </para>
/// <para>
/// The only requirement this places on a call site is that the type it serializes is declared on
/// <see cref="SharpCoreDBJsonContext"/>: under reflection the framework can serialize anything, while the
/// source-generated resolver can only serve the types it was generated for.
/// </para>
/// </summary>
internal static class AotJsonSerializer
{
    /// <summary>
    /// Gets the shared options for call sites that need no formatting: the resolver and settings the call sites
    /// used before AOT-readiness, so on-disk and wire bytes do not change.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Creates options carrying a resolver this platform can serve. <paramref name="writeIndented"/> mirrors
    /// the formatting the call site used before AOT-readiness, so the on-disk bytes do not change.
    /// </summary>
    /// <param name="writeIndented">Whether the writer indents, as the call site did before.</param>
    /// <returns>Options with a usable type-info resolver.</returns>
    internal static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        // Where reflection is disabled (Native AOT / trimming) the source-generated contexts are installed —
        // combined, because the internal state types live in their own context (see SharpCoreDBAotJsonContext).
        // Without them an unset resolver would fail for every type under AOT.
        var resolver = JsonSerializer.IsReflectionEnabledByDefault
            ? CreateReflectionResolver()
            : JsonTypeInfoResolver.Combine(SharpCoreDBJsonContext.Default, SharpCoreDBAotJsonContext.Default);

        return new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            WriteIndented = writeIndented,
        };
    }

    /// <summary>
    /// Creates the framework reflection resolver for the platforms where reflection is available. It has to be
    /// named explicitly: the serializer's convenience overloads fall back to reflection when no resolver is set,
    /// but <see cref="JsonSerializerOptions.GetTypeInfo{T}"/> — the call every site needs for AOT — does not,
    /// and fails with "no metadata for type" instead. The resolver's default settings are the ones those
    /// overloads use, which is what keeps the JSON byte-identical.
    /// </summary>
    /// <returns>The framework's default reflection resolver.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Reached only where JsonSerializer.IsReflectionEnabledByDefault is true; that property is a feature switch which the AOT/trim toolchain folds to false, so this call is not present in a trimmed publish (session-56 worklog entry).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Reached only where JsonSerializer.IsReflectionEnabledByDefault is true; that property is a feature switch which the AOT toolchain folds to false, so the reflection resolver is dead code in a Native AOT publish.")]
    private static IJsonTypeInfoResolver CreateReflectionResolver() => new DefaultJsonTypeInfoResolver();

    /// <summary>Resolves the type metadata for <typeparamref name="T"/> from the AOT-safe options.</summary>
    /// <typeparam name="T">The type to resolve.</typeparam>
    /// <returns>The type metadata the serializer overloads require.</returns>
    internal static JsonTypeInfo<T> TypeInfo<T>() => Options.GetTypeInfo<T>();

    /// <summary>Deserializes a JSON string with the AOT-safe resolver.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="json">The JSON string.</param>
    /// <returns>The deserialized value, or <c>null</c> for a JSON <c>null</c> literal.</returns>
    internal static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

    /// <summary>Deserializes UTF-8 JSON with the AOT-safe resolver.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="utf8Json">The UTF-8 JSON payload.</param>
    /// <returns>The deserialized value, or <c>null</c> for a JSON <c>null</c> literal.</returns>
    internal static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) => JsonSerializer.Deserialize(utf8Json, TypeInfo<T>());

    /// <summary>Serializes a value to a JSON string with the AOT-safe resolver.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON string.</returns>
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

    /// <summary>Serializes a value to UTF-8 JSON with the AOT-safe resolver.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The UTF-8 JSON payload.</returns>
    internal static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>());
}
