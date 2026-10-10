// <copyright file="BlockMetadataCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.Storage.Scdb;
using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// ✅ C# 14: LRU cache for block metadata using Lock class.
/// Phase 3.2: Reduces registry lookups by caching frequently accessed block entries.
/// 
/// Performance: Cache hit = O(1), zero-allocation, MRU move in O(1)
/// Memory: Bounded to MAX_CACHE_SIZE entries
/// Thread-safe: Lock-based synchronization
/// </summary>
/// <remarks>
/// Hot-path notes (2026-09-26):
/// <list type="bullet">
/// <item>A hit performs no allocation and no clock read. The previous implementation stored an
/// <c>AccessTime</c> that no caller ever read, and refreshed it on every hit with
/// <c>DateTime.UtcNow</c> plus a <c>record with { … }</c> clone — one allocation and one syscall
/// per hit, inside the cache lock.</item>
/// <item>The MRU move uses the node returned by <see cref="LinkedList{T}.AddFirst(T)"/> instead of
/// <see cref="LinkedList{T}.Remove(T)"/>, which is an O(n) linear search. A hit near the tail of a
/// 1000-entry list therefore went from ~1000 node visits to 2 pointer writes, all inside the lock —
/// so the lock hold time also drops.</item>
/// </list>
/// LRU semantics are unchanged: the least recently <em>used</em> entry is still the one evicted.
/// </remarks>
public sealed class BlockMetadataCache
{
    private readonly Dictionary<string, CacheEntry> _cache = [];
    private readonly LinkedList<string> _lru = new();
    private readonly Lock _cacheLock = new(); // C# 14
    private const int MAX_CACHE_SIZE = 1000;
    
    // Performance counters
    private long _hits;
    private long _misses;
    
    /// <summary>
    /// Cache entry holding the block metadata and a back-reference to its LRU list node,
    /// so a move-to-front is O(1) instead of a linked-list scan.
    /// </summary>
    private sealed class CacheEntry(BlockEntry entry)
    {
        /// <summary>Gets or sets the cached block metadata (refreshed by a later <c>Add</c>).</summary>
        public BlockEntry Entry { get; set; } = entry;

        /// <summary>Gets the LRU list node that owns this entry's key.</summary>
        public LinkedListNode<string> Node { get; init; } = null!;
    }
    
    /// <summary>
    /// Attempts to retrieve a block entry from cache.
    /// On cache hit, moves entry to front (MRU). Allocation-free.
    /// </summary>
    /// <param name="blockName">Name of the block to retrieve.</param>
    /// <param name="entry">The cached block entry if found.</param>
    /// <returns>True if entry was found in cache, false otherwise.</returns>
    public bool TryGet(string blockName, out BlockEntry entry)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(blockName, out var cached))
            {
                // Move to front (MRU) — O(1) via the stored node, and skipped entirely
                // when the entry already is the most recently used one.
                var node = cached.Node;
                if (!ReferenceEquals(_lru.First, node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                }

                entry = cached.Entry;
                Interlocked.Increment(ref _hits);
                return true;
            }
            
            entry = default;
            Interlocked.Increment(ref _misses);
            return false;
        }
    }
    
    /// <summary>
    /// Adds a block entry to the cache.
    /// If cache is full, evicts the least recently used (LRU) entry.
    /// </summary>
    /// <param name="blockName">Name of the block.</param>
    /// <param name="entry">Block metadata to cache.</param>
    public void Add(string blockName, BlockEntry entry)
    {
        lock (_cacheLock)
        {
            // Check if already exists (update case)
            if (_cache.TryGetValue(blockName, out var existing))
            {
                // Refresh the payload in place; the key is already tracked by the LRU list.
                existing.Entry = entry;

                var node = existing.Node;
                if (!ReferenceEquals(_lru.First, node))
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                }
                return;
            }
            
            // Evict LRU if cache is full
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                var lru = _lru.Last;
                if (lru is not null)
                {
                    _cache.Remove(lru.Value);
                    _lru.RemoveLast();
                }
            }
            
            // Add new entry
            _cache[blockName] = new CacheEntry(entry) { Node = _lru.AddFirst(blockName) };
        }
    }
    
    /// <summary>
    /// Removes a block entry from the cache.
    /// Used when block is deleted or invalidated.
    /// </summary>
    /// <param name="blockName">Name of the block to remove.</param>
    /// <returns>True if entry was removed, false if not found.</returns>
    public bool Remove(string blockName)
    {
        lock (_cacheLock)
        {
            if (_cache.Remove(blockName, out var removed))
            {
                _lru.Remove(removed.Node);
                return true;
            }
            return false;
        }
    }
    
    /// <summary>
    /// Clears all entries from the cache.
    /// Used during database close or cache invalidation.
    /// </summary>
    public void Clear()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }
    
    /// <summary>
    /// Gets cache statistics for monitoring and tuning.
    /// </summary>
    /// <returns>Tuple of (Size, HitRate, Hits, Misses).</returns>
    public (int Size, double HitRate, long Hits, long Misses) GetStatistics()
    {
        lock (_cacheLock)
        {
            var hits = Interlocked.Read(ref _hits);
            var misses = Interlocked.Read(ref _misses);
            var total = hits + misses;
            var hitRate = total > 0 ? (double)hits / total : 0.0;
            
            return (_cache.Count, hitRate, hits, misses);
        }
    }
    
    /// <summary>
    /// Resets performance counters.
    /// Used for benchmarking and testing.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }
}
