// <copyright file="ArtifactVerifier.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore verify.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

using System.Security.Cryptography;

/// <summary>
/// Strict verification, before anything is opened. Manifests and component files are treated as
/// untrusted even when fetched from the configured account: everything here runs before an engine
/// sees a byte and before an untrusted length is used to reserve memory.
/// </summary>
public static class ArtifactVerifier
{
    /// <summary>
    /// Normalize and validate one component path. Rejects empty paths, control characters,
    /// backslashes (paths are POSIX-relative, never host-shaped), absolute paths, drive prefixes,
    /// alternate-data-stream colons, and empty / <c>.</c> / <c>..</c> segments. The returned path is
    /// the normalized relative form used for both the local file and the object key, so the two
    /// cannot disagree about what a path means.
    /// </summary>
    /// <exception cref="ArtifactException">The path is not a normalized relative path.</exception>
    public static string NormalizeComponentPath(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            throw ArtifactException.Path("empty component path");
        }

        foreach (char c in raw)
        {
            if (c == '\0' || c < 0x20)
            {
                throw ArtifactException.Path($"control character in path: {raw}");
            }
        }

        if (raw.Contains('\\', StringComparison.Ordinal))
        {
            throw ArtifactException.Path($"backslash in path (paths are POSIX-relative, never host-shaped): {raw}");
        }

        if (raw[0] == '/')
        {
            throw ArtifactException.Path($"absolute path: {raw}");
        }

        // `C:` or `C:/...`. Checked as a drive prefix specifically, so the check names the right cause.
        if (raw.Length >= 2 && raw[1] == ':' && IsAsciiLetter(raw[0]))
        {
            throw ArtifactException.Path($"drive prefix in path: {raw}");
        }

        foreach (string segment in raw.Split('/'))
        {
            if (segment.Length == 0)
            {
                throw ArtifactException.Path($"empty path segment in: {raw}");
            }

            if (segment == ".")
            {
                throw ArtifactException.Path($"'.' path segment in: {raw}");
            }

            if (segment == "..")
            {
                throw ArtifactException.Path($"'..' path segment in: {raw}");
            }

            if (segment.Contains(':', StringComparison.Ordinal))
            {
                throw ArtifactException.Path($"colon (alternate data stream) in path: {raw}");
            }
        }

        return raw;
    }

    /// <summary>
    /// Validate a manifest's declared structure against the reader's limits and capabilities.
    /// Runs before any component is fetched or allocated.
    /// </summary>
    /// <exception cref="ArtifactException">A limit is exceeded, a path is hostile, the manifest
    /// contradicts itself, or the reader cannot serve the artifact.</exception>
    public static void ValidateManifest(
        int formatVersion,
        IReadOnlyList<ArtifactComponent> components,
        ArtifactRangeMapRef? rangeMap,
        ArtifactLimits limits,
        ReaderCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (formatVersion < capabilities.FormatMin || formatVersion > capabilities.FormatMax)
        {
            throw ArtifactException.Unsupported(
                $"artifact format version {formatVersion} is outside this reader's range " +
                $"{capabilities.FormatMin}..{capabilities.FormatMax}");
        }

        if (components.Count == 0)
        {
            throw ArtifactException.Invalid("manifest declares no components");
        }

        if (components.Count > limits.MaxComponents)
        {
            throw ArtifactException.Limit(
                $"manifest declares {components.Count} components, limit is {limits.MaxComponents}");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (ArtifactComponent component in components)
        {
            string normalized = NormalizeComponentPath(component.Path);

            if (!seen.Add(normalized))
            {
                throw ArtifactException.Invalid($"duplicate component path {normalized}");
            }

            if (component.BytesLen < 0)
            {
                throw ArtifactException.Invalid($"component {normalized} declares a negative length");
            }

            if (component.BytesLen > limits.MaxComponentBytes)
            {
                throw ArtifactException.Limit(
                    $"component {normalized} declares {component.BytesLen} bytes, limit is {limits.MaxComponentBytes}");
            }

            total = checked(total + component.BytesLen);
            if (total > limits.MaxTotalBytes)
            {
                throw ArtifactException.Limit(
                    $"components declare {total} bytes total, limit is {limits.MaxTotalBytes}");
            }

            if (component.Sha256.Length != 64)
            {
                throw ArtifactException.Invalid(
                    $"component {normalized} declares a {component.Sha256.Length}-char sha256 (expected 64)");
            }
        }

        if (rangeMap is not null)
        {
            string normalized = NormalizeComponentPath(rangeMap.Path);
            if (!seen.Contains(normalized))
            {
                throw ArtifactException.Invalid(
                    $"range map {normalized} is not listed as a component, so its bytes are unhashed");
            }

            if (rangeMap.BlockBytes <= 0 || rangeMap.Blocks <= 0)
            {
                throw ArtifactException.Invalid($"range map {normalized} declares a non-positive block size/count");
            }
        }
    }

    /// <summary>
    /// Confirm that manifest bytes hash to the artifact id they are claimed under.
    /// </summary>
    /// <remarks>
    /// Call this on the bytes that were FETCHED, never on a re-serialization of a parsed manifest:
    /// re-serializing would verify this process's encoder against itself and prove nothing about
    /// what was stored.
    /// </remarks>
    /// <exception cref="ArtifactException">The bytes do not hash to the claimed id.</exception>
    public static void VerifyManifestBytes(ReadOnlySpan<byte> bytes, string expectedArtifactId)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedArtifactId);
        string got = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(got, expectedArtifactId, StringComparison.Ordinal))
        {
            throw ArtifactException.Integrity(
                $"manifest bytes hash to {got}, not the expected artifact id {expectedArtifactId}");
        }
    }

    /// <summary>Check one component's bytes against its declared length and hash.</summary>
    /// <exception cref="ArtifactException">The length or hash does not match.</exception>
    public static void VerifyComponent(ArtifactComponent component, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (bytes.Length != component.BytesLen)
        {
            throw ArtifactException.Integrity(
                $"component {component.Path} is {bytes.Length} bytes, manifest declares {component.BytesLen}");
        }

        string got = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(got, component.Sha256, StringComparison.Ordinal))
        {
            throw ArtifactException.Integrity(
                $"component {component.Path} hashes to {got}, manifest declares {component.Sha256}");
        }
    }

    /// <summary>Lowercase-hex SHA-256 of a component's bytes (the value a manifest records).</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsAsciiLetter(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
}
