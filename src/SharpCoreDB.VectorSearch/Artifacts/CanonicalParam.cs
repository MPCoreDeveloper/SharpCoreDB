// <copyright file="CanonicalParam.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore model.rs Param (no float variant).
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A scalar a hashed artifact document may carry. Deliberately has <b>no float variant</b>: the
/// canonicalizer would refuse one, and refusing at the type is better than refusing at the hash.
/// A genuine ratio is carried as a decimal <see cref="Text"/> at a declared scale — the same trick
/// the munarium datastore uses for graph <c>alpha</c>.
/// </summary>
[JsonConverter(typeof(CanonicalParamJsonConverter))]
public abstract record CanonicalParam
{
    private CanonicalParam()
    {
    }

    /// <summary>A signed integer value.</summary>
    public sealed record Int(long Value) : CanonicalParam;

    /// <summary>A string value (also the carrier for a ratio at a declared scale).</summary>
    public sealed record Text(string Value) : CanonicalParam;

    /// <summary>A boolean value.</summary>
    public sealed record Bool(bool Value) : CanonicalParam;

    /// <summary>An explicit null.</summary>
    public sealed record Null : CanonicalParam;

    /// <summary>Wraps a <see cref="long"/>.</summary>
    public static CanonicalParam From(long value) => new Int(value);

    /// <summary>Wraps an <see cref="int"/>.</summary>
    public static CanonicalParam From(int value) => new Int(value);

    /// <summary>Wraps a <see cref="string"/>.</summary>
    public static CanonicalParam From(string value) => new Text(value);

    /// <summary>Wraps a <see cref="bool"/>.</summary>
    public static CanonicalParam From(bool value) => new Bool(value);

    /// <summary>
    /// Carries a ratio as a round-trippable, culture-invariant decimal STRING. This is the ONLY
    /// sanctioned way to put a fractional value into a hashed document.
    /// </summary>
    public static CanonicalParam Ratio(double value)
        => new Text(value.ToString("R", CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static implicit operator CanonicalParam(long value) => new Int(value);

    /// <inheritdoc />
    public static implicit operator CanonicalParam(int value) => new Int(value);

    /// <inheritdoc />
    public static implicit operator CanonicalParam(string value) => new Text(value);

    /// <inheritdoc />
    public static implicit operator CanonicalParam(bool value) => new Bool(value);
}

/// <summary>
/// Writes a <see cref="CanonicalParam"/> as the underlying JSON scalar (not as
/// <c>{"Value":…}</c>), so a parameter map hashes as <c>{"M":16}</c> rather than as a wrapper object.
/// </summary>
public sealed class CanonicalParamJsonConverter : JsonConverter<CanonicalParam>
{
    /// <inheritdoc />
    public override CanonicalParam Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt64(out long value) => new CanonicalParam.Int(value),
            JsonTokenType.Number => throw ArtifactException.Canonical(
                "artifact profile forbids non-integer numbers; carry a ratio as a decimal STRING"),
            JsonTokenType.String => new CanonicalParam.Text(reader.GetString() ?? string.Empty),
            JsonTokenType.True => new CanonicalParam.Bool(true),
            JsonTokenType.False => new CanonicalParam.Bool(false),
            JsonTokenType.Null => new CanonicalParam.Null(),
            _ => throw ArtifactException.Invalid($"Unexpected token {reader.TokenType} for CanonicalParam"),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CanonicalParam value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (value)
        {
            case CanonicalParam.Int i:
                writer.WriteNumberValue(i.Value);
                break;
            case CanonicalParam.Text t:
                writer.WriteStringValue(t.Value);
                break;
            case CanonicalParam.Bool b:
                writer.WriteBooleanValue(b.Value);
                break;
            case CanonicalParam.Null:
                writer.WriteNullValue();
                break;
            default:
                throw ArtifactException.Invalid($"Unsupported CanonicalParam '{value.GetType().Name}'");
        }
    }
}
