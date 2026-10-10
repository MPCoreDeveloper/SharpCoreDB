// <copyright file="ArtifactCacheKey.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore lib.rs ArtifactCacheKey.
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// The full residency key for a hydrated artifact: isolation domain + logical version + content id.
/// </summary>
/// <remarks>
/// An artifact id is a content identifier and <b>never an authority</b>. A byte-identical manifest can
/// legitimately occur in two tenants, so residency, hydration, eviction and quarantine all key on the
/// full <see cref="ArtifactCacheKey"/>. Coalescing on the bare hash would let one tenant's request
/// warm, evict or quarantine another tenant's cache entry.
/// </remarks>
/// <param name="IsolationDomain">The tenant / isolation domain.</param>
/// <param name="LogicalVersionId">The logical version id (engine-independent).</param>
/// <param name="ArtifactId">The physical content id (lowercase hex).</param>
public sealed record ArtifactCacheKey(string IsolationDomain, string LogicalVersionId, string ArtifactId)
{
    /// <summary>
    /// The cache-relative path for this key. Every element is checked rather than trusted: the values
    /// arrive from the caller, and a <c>..</c> in any would otherwise walk out of the cache root.
    /// </summary>
    public string L1RelativePath()
    {
        foreach ((string label, string part) in new[]
        {
            ("IsolationDomain", IsolationDomain),
            ("LogicalVersionId", LogicalVersionId),
            ("ArtifactId", ArtifactId),
        })
        {
            if (string.IsNullOrEmpty(part))
            {
                throw ArtifactException.Path($"{label} is empty");
            }

            foreach (char c in part)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                {
                    throw ArtifactException.Path($"{label} {part} may hold only [A-Za-z0-9_-]; it becomes a path element");
                }
            }
        }

        return $"{IsolationDomain}/{LogicalVersionId}/{ArtifactId}";
    }

    /// <summary>Deliberately shows only a PREFIX of the ids and never the domain in full: this ends up in logs.</summary>
    public override string ToString()
        => $"artifact({Short(IsolationDomain)}…/{LogicalVersionId}/{Short(ArtifactId)}…)";

    private static string Short(string value)
        => value.Length <= 12 ? value : value[..12];
}
