// <copyright file="CanonicalJson.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore canonical.rs (artifact@1).
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// RFC 8785 (JCS) canonical JSON, with one deliberate restriction: <b>floating-point numbers
/// are refused</b> rather than formatted. JCS number canonicalization is ES6
/// <c>Number::toString</c>, and it is the part implementations get wrong — shortest round-trip
/// formatting, exponent thresholds, negative zero. Nothing in a hashed artifact document needs a
/// float: dimensions, byte lengths, counts and positions are integers, and a genuine ratio is
/// carried as a decimal STRING at a declared scale (see <see cref="CanonicalParam.Ratio"/>).
/// </summary>
/// <remarks>
/// Rules implemented here:
/// <list type="bullet">
/// <item>Object members are sorted by UTF-16 code unit (not by ordinal code point).</item>
/// <item>Numbers must be integers; anything else throws <see cref="ArtifactErrorKind.Canonical"/>.</item>
/// <item>Nulls are emitted explicitly — an omitted member and a null member are different documents.</item>
/// <item>Strings get minimal escaping; non-ASCII is emitted literally and never normalized.</item>
/// <item>Array order is content and is preserved.</item>
/// </list>
/// </remarks>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = false,
        // Deliberately no DefaultIgnoreCondition: artifact documents serialize every member,
        // including nulls. `WhenWritingNull` would silently change what an artifact is called.
    };

    /// <summary>Canonical UTF-8 bytes of a value under the artifact profile.</summary>
    /// <exception cref="ArtifactException">A value cannot be canonicalized (e.g. a float).</exception>
    public static byte[] CanonicalBytes<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is JsonElement element)
        {
            return Encoding.UTF8.GetBytes(Canonicalize(element));
        }

        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, SerializeOptions);
        using var doc = JsonDocument.Parse(utf8);
        return Encoding.UTF8.GetBytes(Canonicalize(doc.RootElement));
    }

    /// <summary>Canonical JSON text of a value.</summary>
    public static string CanonicalString<T>(T value) => Encoding.UTF8.GetString(CanonicalBytes(value));

    /// <summary>Lowercase-hex SHA-256 of the canonical bytes. No prefix, no newline, no domain separator.</summary>
    public static string CanonicalSha256<T>(T value)
        => Convert.ToHexString(SHA256.HashData(CanonicalBytes(value))).ToLowerInvariant();

    /// <summary>Canonical JSON text of a parsed <see cref="JsonElement"/>.</summary>
    public static string Canonicalize(JsonElement element)
    {
        var sb = new StringBuilder(256);
        Write(element, sb, "$");
        return sb.ToString();
    }

    private static void Write(JsonElement element, StringBuilder sb, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var members = new List<JsonProperty>();
                foreach (var p in element.EnumerateObject())
                {
                    members.Add(p);
                }

                members.Sort(static (a, b) => CompareUtf16(a.Name, b.Name));
                sb.Append('{');
                for (int i = 0; i < members.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    WriteString(members[i].Name, sb);
                    sb.Append(':');
                    Write(members[i].Value, sb, path + "." + members[i].Name);
                }

                sb.Append('}');
                break;
            }

            case JsonValueKind.Array:
            {
                sb.Append('[');
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index > 0)
                    {
                        sb.Append(',');
                    }

                    Write(item, sb, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                    index++;
                }

                sb.Append(']');
                break;
            }

            case JsonValueKind.String:
                WriteString(element.GetString() ?? string.Empty, sb);
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long signedValue))
                {
                    sb.Append(signedValue.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                if (element.TryGetUInt64(out ulong unsignedValue))
                {
                    sb.Append(unsignedValue.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                throw ArtifactException.Canonical(
                    $"{path}: artifact profile forbids non-integer numbers (got {element.GetRawText()}); " +
                    "carry a ratio as a decimal STRING at a declared scale");

            case JsonValueKind.True:
                sb.Append("true");
                break;

            case JsonValueKind.False:
                sb.Append("false");
                break;

            case JsonValueKind.Null:
                sb.Append("null");
                break;

            default:
                throw ArtifactException.Canonical($"{path}: unsupported JSON value kind {element.ValueKind}");
        }
    }

    /// <summary>
    /// Minimal escaping: the short forms where they exist and lowercase <c>\u00XX</c> otherwise.
    /// Non-ASCII is emitted literally — Unicode is never normalized, so the UTF-8 bytes of a string
    /// are hashed as given.
    /// </summary>
    private static void WriteString(string value, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>
    /// JCS sorts object member names by UTF-16 code unit. A C# <see cref="char"/> IS a UTF-16 code
    /// unit, so comparing them directly is exactly that order — and it differs from ordinal
    /// (code-point) comparison above the BMP.
    /// </summary>
    private static int CompareUtf16(string a, string b)
    {
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            int delta = a[i] - b[i];
            if (delta != 0)
            {
                return delta;
            }
        }

        return a.Length - b.Length;
    }
}
