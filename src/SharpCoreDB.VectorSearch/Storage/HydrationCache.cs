// <copyright file="HydrationCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore hydrate.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Storage;

using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>Where an artifact currently is.</summary>
public enum Residency
{
    /// <summary>Nothing on disk.</summary>
    Absent,

    /// <summary>A partial download exists (a crash or an in-flight hydration).</summary>
    Partial,

    /// <summary>Hydrated and sealed (COMPLETE present).</summary>
    Resident,

    /// <summary>A verification failure quarantined this key.</summary>
    Quarantined,
}

/// <summary>A hydrated, sealed artifact.</summary>
public sealed record ResidentArtifact(string ArtifactId, string Path, long Bytes);

/// <summary>The cache's size ceiling. When exceeded, resident artifacts are evicted oldest-first.</summary>
public sealed record CacheBudget(long HighWatermarkBytes)
{
    /// <summary>The default 256 MiB ceiling.</summary>
    public static CacheBudget Default { get; } = new(256L * 1024 * 1024);
}

/// <summary>One component's verified bytes, as produced by a fetch delegate.</summary>
public sealed record ComponentBytes(ArtifactComponent Component, byte[] Bytes);

/// <summary>
/// L1 hydration: brings an artifact from its store to a local directory, verifying as it goes, and
/// reclaims space when the cache grows past its high watermark.
/// </summary>
/// <remarks>
/// Hydration is <b>single-flight per key</b> and writes into a <c>.partial</c> directory, verifies
/// every component, fsyncs each file, then atomically renames to the sealed leaf and writes
/// <c>COMPLETE</c> last. A reader therefore never observes a half-written artifact at a sealed path,
/// and a crash leaves a <c>.partial</c> directory rather than a plausible-looking broken artifact.
/// A verification failure quarantines the key.
/// </remarks>
public sealed partial class HydrationCache
{
    /// <summary>The marker written LAST into a sealed artifact directory.</summary>
    public const string CompleteMarker = "COMPLETE";

    private readonly string _root;
    private readonly CacheBudget _budget;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Lazy<ResidentArtifact>> _inFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _quarantined = new(StringComparer.Ordinal);

    /// <summary>Initialize the cache rooted at a directory.</summary>
    public HydrationCache(string root, CacheBudget? budget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _budget = budget ?? CacheBudget.Default;
    }

    /// <summary>Gets the resolved cache root.</summary>
    public string Root => _root;

    /// <summary>Gets the current state of a key.</summary>
    public Residency StateOf(ArtifactCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string relative = key.L1RelativePath();

        lock (_lock)
        {
            if (_quarantined.Contains(relative))
            {
                return Residency.Quarantined;
            }
        }

        string leaf = Leaf(key);
        if (File.Exists(Path.Combine(leaf, CompleteMarker)))
        {
            return Residency.Resident;
        }

        return Directory.Exists(leaf) || Directory.Exists(leaf + ".partial")
            ? Residency.Partial
            : Residency.Absent;
    }

    /// <summary>
    /// Hydrate a key, verifying every fetched component. Two concurrent calls for the same key share
    /// one hydration.
    /// </summary>
    /// <param name="key">The residency key.</param>
    /// <param name="fetch">Produces the components and their bytes.</param>
    public ResidentArtifact Hydrate(ArtifactCacheKey key, Func<IReadOnlyList<ComponentBytes>> fetch)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fetch);

        string relative = key.L1RelativePath();

        lock (_lock)
        {
            if (_quarantined.Contains(relative))
            {
                throw ArtifactException.Integrity($"{relative} is quarantined; refusing to re-hydrate");
            }
        }

        // Already sealed: nothing to fetch, nothing to verify again.
        if (StateOf(key) == Residency.Resident)
        {
            string sealedPath = Leaf(relative);
            long sealedBytes = Directory.EnumerateFiles(sealedPath, "*", SearchOption.AllDirectories)
                .Sum(static f => new FileInfo(f).Length);
            return new ResidentArtifact(key.ArtifactId, sealedPath, sealedBytes);
        }

        Lazy<ResidentArtifact> work;
        lock (_lock)
        {
            if (!_inFlight.TryGetValue(relative, out Lazy<ResidentArtifact>? existing))
            {
                existing = new Lazy<ResidentArtifact>(
                    () => HydrateCore(key, relative, fetch),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _inFlight[relative] = existing;
            }

            work = existing;
        }

        try
        {
            return work.Value;
        }
        finally
        {
            lock (_lock)
            {
                _inFlight.Remove(relative);
            }
        }
    }

    /// <summary>The sealed directory for a key.</summary>
    public string Leaf(ArtifactCacheKey key)
        => Path.Combine(_root, key.IsolationDomain, key.LogicalVersionId, key.ArtifactId);

    private string Leaf(string relative)
    {
        string[] parts = relative.Split('/');
        return Path.Combine(_root, parts[0], parts[1], parts[2]);
    }

    /// <summary>Delete any partial directory left by a crash (call on startup).</summary>
    public int ReconcilePartials()
    {
        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(_root, "*.partial", SearchOption.AllDirectories))
        {
            Directory.Delete(directory, recursive: true);
            removed++;
        }

        return removed;
    }
}
