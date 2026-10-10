// <copyright file="HydrationCache.Evict.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Storage;

using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>Hydration core and eviction for <see cref="HydrationCache"/>.</summary>
public sealed partial class HydrationCache
{
    private ResidentArtifact HydrateCore(ArtifactCacheKey key, string relative, Func<IReadOnlyList<ComponentBytes>> fetch)
    {
        string leaf = Leaf(relative);
        string partial = leaf + ".partial";

        if (Directory.Exists(partial))
        {
            Directory.Delete(partial, recursive: true);
        }

        Directory.CreateDirectory(partial);

        long total = 0;
        try
        {
            foreach (ComponentBytes item in fetch())
            {
                // Length and hash are both checked BEFORE anything is sealed.
                ArtifactVerifier.VerifyComponent(item.Component, item.Bytes);
                total += item.Bytes.Length;

                string target = Path.Combine(partial, item.Component.Path.Replace('/', Path.DirectorySeparatorChar));
                string? directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(item.Bytes);
                stream.Flush(flushToDisk: true);
            }

            // COMPLETE is written LAST inside .partial, so a sealed leaf always means "fully verified".
            File.WriteAllText(Path.Combine(partial, CompleteMarker), key.ArtifactId);

            if (Directory.Exists(leaf))
            {
                Directory.Delete(leaf, recursive: true);
            }

            string? parent = Path.GetDirectoryName(leaf);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            Directory.Move(partial, leaf);

            EvictIfNeeded(total);
            return new ResidentArtifact(key.ArtifactId, leaf, total);
        }
        catch (ArtifactException)
        {
            TryDelete(partial);
            lock (_lock)
            {
                _quarantined.Add(relative);
            }

            throw;
        }
        catch (IOException)
        {
            TryDelete(partial);
            throw;
        }
    }

    private void EvictIfNeeded(long incoming)
    {
        var leaves = new List<(string Path, long Bytes, DateTime LastWrite)>();
        foreach (string marker in Directory.EnumerateFiles(_root, CompleteMarker, SearchOption.AllDirectories))
        {
            string directory = Path.GetDirectoryName(marker)!;
            long bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(static f => new FileInfo(f).Length);
            leaves.Add((directory, bytes, Directory.GetLastWriteTimeUtc(directory)));
        }

        long total = leaves.Sum(static l => l.Bytes);
        if (total + incoming <= _budget.HighWatermarkBytes)
        {
            return;
        }

        // Oldest write first, deterministically.
        leaves.Sort(static (a, b) => a.LastWrite.CompareTo(b.LastWrite));
        foreach ((string path, long bytes, _) in leaves)
        {
            if (total + incoming <= _budget.HighWatermarkBytes)
            {
                break;
            }

            TryDelete(path);
            total -= bytes;
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Eviction is best-effort; a locked directory is retried on the next hydration.
        }
    }
}
