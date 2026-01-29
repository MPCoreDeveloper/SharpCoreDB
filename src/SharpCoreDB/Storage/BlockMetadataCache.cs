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
/// Performance: Cache hit = O(1), Cache miss = Registry lookup
/// Memory: Bounded to MAX_CACHE_SIZE entries
/// Thread-safe: Lock-based synchronization
/// </summary>
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
    /// Cache entry containing block metadata and access timestamp.
    /// C# 14: Record type with immutable properties.
    /// </summary>
    private sealed record CacheEntry(BlockEntry Entry, DateTime AccessTime);
    
    /// <summary>
    /// Attempts to retrieve a block entry from cache.
    /// On cache hit, moves entry to front (MRU) and updates access time.
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
                // Move to front (MRU)
                _lru.Remove(blockName);
                _lru.AddFirst(blockName);
                
                // Update access time using 'with' expression (C# 14)
                _cache[blockName] = cached with { AccessTime = DateTime.UtcNow };
                
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
            if (_cache.ContainsKey(blockName))
            {
                // Update existing entry
                _lru.Remove(blockName);
                _lru.AddFirst(blockName);
                _cache[blockName] = new CacheEntry(entry, DateTime.UtcNow);
                return;
            }
            
            // Evict LRU if cache is full
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                var lru = _lru.Last!.Value;
                _cache.Remove(lru);
                _lru.RemoveLast();
            }
            
            // Add new entry
            _cache[blockName] = new CacheEntry(entry, DateTime.UtcNow);
            _lru.AddFirst(blockName);
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
            if (_cache.Remove(blockName))
            {
                _lru.Remove(blockName);
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
