// <copyright file="SingleFileTableJsonContext.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// AOT-safe, source-generated JSON context for the single-file table row cache, which is a
/// polymorphic <c>List&lt;Dictionary&lt;string, object?&gt;&gt;</c>. Reflection-based
/// System.Text.Json serialization is disabled under Native AOT / trimming, so every runtime
/// value type that can appear in a row is declared here. The generated resolver is combined
/// with <see cref="PolymorphicObjectConverter"/> on the options used by SingleFileTable.
/// </summary>
[JsonSerializable(typeof(List<Dictionary<string, object?>>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(float[]))]
internal sealed partial class SingleFileTableJsonContext : JsonSerializerContext
{
}

/// <summary>
/// AOT-safe converter for polymorphic <c>object</c> values in the single-file row cache.
/// Serializes values through their runtime type via the source-generated context (the exact
/// same JSON output as the previous reflection serializer); deserializes to
/// <see cref="JsonElement"/>, which <c>SingleFileTable.FromSerializableRow</c> converts to
/// CLR types exactly as before.
/// </summary>
internal sealed class PolymorphicObjectConverter : JsonConverter<object?>
{
    public static readonly PolymorphicObjectConverter Instance = new();

    private PolymorphicObjectConverter()
    {
    }

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<JsonElement>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
