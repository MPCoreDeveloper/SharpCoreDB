// <copyright file="LocalFileStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore store.rs LocalFileStore.
// </copyright>

namespace SharpCoreDB.VectorSearch.Store;

using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// The local-filesystem artifact store.
/// </summary>
/// <remarks>
/// Component paths are re-normalized here rather than trusted from the caller. This is the last
/// point before a real filesystem operation, and a check that only runs at parse time is a check an
/// alternative code path can skip.
/// </remarks>
public sealed class LocalFileStore : IArtifactStore
{
    private readonly string _root;

    /// <summary>Root the store at a directory; the directory is created if absent.</summary>
    /// <param name="root">The root directory.</param>
    public LocalFileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Gets the resolved root directory.</summary>
    public string Root => _root;

    /// <inheritdoc />
    public void PutComponent(string path, ReadOnlySpan<byte> bytes)
    {
        string target = Resolve(path);
        string? directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    /// <inheritdoc />
    public byte[] GetComponent(string path, ByteRange? range = null)
    {
        string target = Resolve(path);

        if (range is null)
        {
            return File.ReadAllBytes(target);
        }

        ByteRange value = range.Value;
        if (value.End < value.Start)
        {
            throw ArtifactException.Invalid($"inverted range {value.Start}..{value.End} for {path}");
        }

        using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(value.Start, SeekOrigin.Begin);
        var buffer = new byte[value.End - value.Start];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <inheritdoc />
    public long HeadComponent(string path) => new FileInfo(Resolve(path)).Length;

    /// <inheritdoc />
    public bool Exists(string path) => File.Exists(Resolve(path));

    private string Resolve(string path)
    {
        string normalized = ArtifactVerifier.NormalizeComponentPath(path);
        return Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar));
    }
}
