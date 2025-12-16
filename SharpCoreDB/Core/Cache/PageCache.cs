// <copyright file="PageCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.Cache;

/// <summary>
/// High-performance page cache with CLOCK eviction algorithm.
/// Thread-safe and lock-free for most operations.
/// 
/// <para>
/// <b>Architecture:</b>
/// This class is split into multiple partial files for better organization:
/// </para>
/// 
/// <list type="bullet">
/// <item>
/// <term>PageCache.Core.cs</term>
/// <description>Fields, constructor, properties, and disposal logic</description>
/// </item>
/// <item>
/// <term>PageCache.Operations.cs</term>
/// <description>Public cache operations (Get, Pin, Unpin, Flush, Evict, Clear)</description>
/// </item>
/// <item>
/// <term>PageCache.Algorithms.cs</term>
/// <description>CLOCK eviction algorithm and internal helper methods</description>
/// </item>
/// </list>
/// 
/// <para>
/// <b>Key Features:</b>
/// </para>
/// <list type="bullet">
/// <item>Lock-free operations using ConcurrentDictionary and Interlocked operations</item>
/// <item>CLOCK eviction algorithm for efficient page replacement</item>
/// <item>Page pinning mechanism to prevent eviction of active pages</item>
/// <item>Dirty page tracking with selective flushing</item>
/// <item>Comprehensive statistics for monitoring and diagnostics</item>
/// <item>Memory pooling for efficient buffer management</item>
/// </list>
/// 
/// <para>
/// <b>Thread Safety:</b>
/// All public methods are thread-safe. The cache uses lock-free techniques
/// where possible, falling back to lightweight latching when necessary.
/// </para>
/// 
/// <para>
/// <b>C# 14 Features:</b>
/// </para>
/// <list type="bullet">
/// <item>ArgumentNullException.ThrowIfNull for parameter validation</item>
/// <item>ArgumentOutOfRangeException.ThrowIfNegativeOrZero for range checks</item>
/// <item>Target-typed new expressions for concise initialization</item>
/// <item>Enhanced pattern matching with is/is not patterns</item>
/// <item>Tuple deconstruction in foreach loops</item>
/// <item>Pattern matching with constants</item>
/// </list>
/// 
/// <para>
/// <b>Usage Example:</b>
/// <code>
/// var cache = new PageCache(capacity: 1024, pageSize: 4096);
/// 
/// // Get a page (automatically pins it)
/// var frame = cache.GetPage(pageId, loadFunc: id => LoadFromDisk(id));
/// 
/// // Use the page
/// DoSomethingWith(frame.Buffer);
/// 
/// // Mark as dirty if modified
/// cache.MarkDirty(pageId);
/// 
/// // Unpin when done
/// cache.UnpinPage(pageId);
/// 
/// // Flush dirty pages
/// cache.FlushAll((id, data) => WriteToDisk(id, data));
/// </code>
/// </para>
/// </summary>
public sealed partial class PageCache
{
}
