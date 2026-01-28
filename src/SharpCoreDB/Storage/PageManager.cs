// <copyright file="PageManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

/// <summary>
/// PageManager handles page-level storage operations for the hybrid storage engine.
/// Uses fixed-size pages (8KB default) with slot-array record layout (SQLite-style).
/// </summary>
public partial class PageManager : IDisposable
{
    /// <summary>
    /// Represents a unique page identifier (file offset).
    /// </summary>
    public readonly struct PageId : IEquatable<PageId>
    {
        /// <summary>The unique page ID value (typically file offset / 8KB).</summary>
        public readonly ulong Value;

        /// <summary>Initializes a new instance of the <see cref="PageId"/> struct.</summary>
        public PageId(ulong value) => Value = value;

        public bool IsValid => Value != 0;
        public static PageId Invalid => new(ulong.MaxValue);
        public override bool Equals(object? obj) => obj is PageId pageId && Equals(pageId);
        public bool Equals(PageId other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(PageId left, PageId right) => left.Value == right.Value;
        public static bool operator !=(PageId left, PageId right) => left.Value != right.Value;
        public override string ToString() => $"PageId({Value})";
    }

    /// <summary>
    /// Represents a record ID (slot within a page).
    /// </summary>
    public readonly struct RecordId : IEquatable<RecordId>
    {
        /// <summary>The slot index within the page.</summary>
        public readonly ushort SlotIndex;

        /// <summary>Initializes a new instance of the <see cref="RecordId"/> struct.</summary>
        public RecordId(ushort slotIndex) => SlotIndex = slotIndex;

        public bool IsValid => SlotIndex != ushort.MaxValue;
        public override bool Equals(object? obj) => obj is RecordId recordId && Equals(recordId);
        public bool Equals(RecordId other) => SlotIndex == other.SlotIndex;
        public override int GetHashCode() => SlotIndex.GetHashCode();
        public static bool operator ==(RecordId left, RecordId right) => left.SlotIndex == right.SlotIndex;
        public static bool operator !=(RecordId left, RecordId right) => left.SlotIndex != right.SlotIndex;
        public override string ToString() => $"RecordId({SlotIndex})";
    }

    /// <summary>Represents page type classification.</summary>
    public enum PageType : byte
    {
        /// <summary>Free page available for allocation.</summary>
        Free = 0,
        /// <summary>Page containing table data (normal rows).</summary>
        Table = 1,
        /// <summary>Page containing index data.</summary>
        Index = 2,
        /// <summary>Page containing free space map.</summary>
        FreeSpaceMap = 3,
        /// <summary>Page containing metadata.</summary>
        Metadata = 4
    }

    /// <summary>Flags for record state management.</summary>
    [Flags]
    public enum RecordFlags : byte
    {
        /// <summary>Record is active and valid.</summary>
        Active = 0,
        /// <summary>Record has been deleted (soft delete).</summary>
        Deleted = 1,
        /// <summary>Record has changed since last checkpoint.</summary>
        Modified = 2,
        /// <summary>Record is locked by a transaction.</summary>
        Locked = 4
    }

    // Page layout constants
    protected const int PAGE_SIZE = 8192;
    protected const int PAGE_HEADER_SIZE = 32;
    protected const int SLOT_SIZE = 4;
    protected const int MIN_RECORD_SIZE = 16;
    protected const int MAX_RECORD_SIZE = PAGE_SIZE - PAGE_HEADER_SIZE - SLOT_SIZE;

    // Storage fields
    protected FileStream? pagesFile;
    protected Lock writeLock = new();
    
    // Cache implementation
    private readonly Dictionary<ulong, Page> pageCache = new();
    private readonly LinkedList<ulong> lruList = new();
    private readonly Dictionary<ulong, LinkedListNode<ulong>> lruNodeMap = new();
    private readonly HashSet<ulong> dirtyPages = new();
    private readonly int cacheCapacity;
    private ulong nextPageId = 1;
    
    // Track pages per table for FindPageWithSpace
    private readonly Dictionary<uint, List<ulong>> tablePagesIndex = new();
    
    // Cache statistics
    private long cacheHits = 0;
    private long cacheMisses = 0;
    
    protected internal sealed class FreePageBitmap(int maxPages)
    {
        private readonly System.Collections.BitArray bitmap = new(maxPages);
        private readonly Lock bitmapLock = new();
        
        public void MarkAllocated(ulong pageId) { lock (bitmapLock) if (pageId < (ulong)bitmap.Length) bitmap[(int)pageId] = true; }
        public void MarkFree(ulong pageId) { lock (bitmapLock) if (pageId < (ulong)bitmap.Length) bitmap[(int)pageId] = false; }
        public bool IsAllocated(ulong pageId) { lock (bitmapLock) return pageId >= (ulong)bitmap.Length || bitmap[(int)pageId]; }
    }
    protected internal FreePageBitmap? freePageBitmap;

    /// <summary>Represents an in-memory page structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Page
    {
        /// <summary>Table ID this page belongs to.</summary>
        public uint TableId;
        /// <summary>Page type classification.</summary>
        public PageType Type;
        /// <summary>Number of records in this page.</summary>
        public ushort RecordCount;
        /// <summary>Available free space in bytes.</summary>
        public ushort FreeSpace;
        /// <summary>Page ID.</summary>
        public ulong PageId;
        /// <summary>Whether page has been modified.</summary>
        public bool IsDirty;
        /// <summary>Page data (8KB minus header).</summary>
        public Memory<byte> Data;
        /// <summary>Slot metadata (offset and length for each record).</summary>
        public List<(ushort offset, ushort length, RecordFlags flags)> Slots;
    }

    public PageManager()
    {
        cacheCapacity = 1024;
    }

    public PageManager(string databasePath, uint tableId)
    {
        cacheCapacity = 1024;
        var pagesFilePath = Path.Combine(databasePath, $"table_{tableId}.pages");
        pagesFile = new FileStream(pagesFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public PageManager(string databasePath, uint tableId, DatabaseConfig? config)
    {
        cacheCapacity = config?.PageCacheCapacity ?? 1024;
        var pagesFilePath = Path.Combine(databasePath, $"table_{tableId}.pages");
        pagesFile = new FileStream(pagesFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    protected virtual Page ReadPage(PageId pageId)
    {
        if (pagesFile == null || pageId.Value == 0)
            return new();
            
        lock (writeLock)
        {
            var offset = (long)((pageId.Value - 1) * PAGE_SIZE);
            if (offset >= pagesFile.Length)
                return new();
                
            pagesFile.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[PAGE_SIZE];
            var bytesRead = pagesFile.Read(buffer, 0, PAGE_SIZE);
            
            if (bytesRead < PAGE_HEADER_SIZE)
                return new();
            
            var page = new Page
            {
                TableId = BitConverter.ToUInt32(buffer, 0),
                Type = (PageType)buffer[4],
                RecordCount = BitConverter.ToUInt16(buffer, 5),
                FreeSpace = BitConverter.ToUInt16(buffer, 7),
                PageId = pageId.Value,
                IsDirty = false,
                Data = new Memory<byte>(buffer, PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE),
                Slots = new List<(ushort offset, ushort length, RecordFlags flags)>()
            };
            
            // Read slot metadata from the end of the page
            var slotAreaStart = PAGE_HEADER_SIZE + (PAGE_SIZE - PAGE_HEADER_SIZE) - (page.RecordCount * SLOT_SIZE);
            for (int i = 0; i < page.RecordCount; i++)
            {
                var slotOffset = slotAreaStart + (i * SLOT_SIZE);
                var offset16 = BitConverter.ToUInt16(buffer, slotOffset);
                var length = BitConverter.ToUInt16(buffer, slotOffset + 2);
                page.Slots.Add((offset16, length, RecordFlags.Active));
            }
            
            return page;
        }
    }
    
    protected virtual void WritePage(PageId pageId, Page page)
    {
        if (pagesFile == null || pageId.Value == 0)
            return;
            
        lock (writeLock)
        {
            var offset = (long)((pageId.Value - 1) * PAGE_SIZE);
            var buffer = new byte[PAGE_SIZE];
            
            // Write header
            BitConverter.GetBytes(page.TableId).CopyTo(buffer, 0);
            buffer[4] = (byte)page.Type;
            BitConverter.GetBytes(page.RecordCount).CopyTo(buffer, 5);
            BitConverter.GetBytes(page.FreeSpace).CopyTo(buffer, 7);
            
            // Write data
            page.Data.Span.CopyTo(buffer.AsSpan(PAGE_HEADER_SIZE));
            
            // Write slot metadata
            var slotAreaStart = PAGE_HEADER_SIZE + (PAGE_SIZE - PAGE_HEADER_SIZE) - (page.RecordCount * SLOT_SIZE);
            for (int i = 0; i < page.Slots.Count && i < page.RecordCount; i++)
            {
                var slotOffset = slotAreaStart + (i * SLOT_SIZE);
                BitConverter.GetBytes(page.Slots[i].offset).CopyTo(buffer, slotOffset);
                BitConverter.GetBytes(page.Slots[i].length).CopyTo(buffer, slotOffset + 2);
            }
            
            pagesFile.Seek(offset, SeekOrigin.Begin);
            pagesFile.Write(buffer, 0, PAGE_SIZE);
            pagesFile.Flush();
        }
    }
    
    public virtual PageId AllocatePage(uint tableId, PageType pageType)
    {
        lock (writeLock)
        {
            var pageId = new PageId(nextPageId++);
            var page = new Page
            {
                TableId = tableId,
                Type = pageType,
                RecordCount = 0,
                FreeSpace = (ushort)(PAGE_SIZE - PAGE_HEADER_SIZE),
                PageId = pageId.Value,
                IsDirty = true,
                Data = new Memory<byte>(new byte[PAGE_SIZE - PAGE_HEADER_SIZE]),
                Slots = new List<(ushort offset, ushort length, RecordFlags flags)>()
            };
            
            pageCache[pageId.Value] = page;
            UpdateLRU(pageId.Value);
            dirtyPages.Add(pageId.Value);
            
            // Track this page for the table
            if (!tablePagesIndex.ContainsKey(tableId))
            {
                tablePagesIndex[tableId] = new List<ulong>();
            }
            tablePagesIndex[tableId].Add(pageId.Value);
            
            return pageId;
        }
    }
    
    protected virtual PageId AllocatePageInternal(uint tableId, PageType pageType) => new(0);
    public virtual PageId AllocatePagePublic(uint tableId, PageType pageType) => AllocatePage(0, pageType);
    public virtual void FreePage(PageId pageId) { }

    public virtual PageId FindPageWithSpace(uint tableId, int requiredSpace)
    {
        lock (writeLock)
        {
            // Look for existing pages with enough space
            if (tablePagesIndex.TryGetValue(tableId, out var pageIds))
            {
                foreach (var pid in pageIds)
                {
                    if (pageCache.TryGetValue(pid, out var page))
                    {
                        if (page.FreeSpace >= requiredSpace + SLOT_SIZE)
                        {
                            return new PageId(pid);
                        }
                    }
                    else
                    {
                        // Load page from disk to check space
                        page = ReadPage(new PageId(pid));
                        if (page.PageId != 0 && page.FreeSpace >= requiredSpace + SLOT_SIZE)
                        {
                            pageCache[pid] = page;
                            UpdateLRU(pid);
                            return new PageId(pid);
                        }
                    }
                }
            }
            
            // No page with enough space, allocate a new one
            return AllocatePage(tableId, PageType.Table);
        }
    }
    
    public virtual RecordId InsertRecord(PageId pageId, byte[] data)
    {
        if (pageId.Value == 0 || data.Length > MAX_RECORD_SIZE)
            return new(0);
            
        lock (writeLock)
        {
            // Get or load page
            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return new(0);
                    
                pageCache[pageId.Value] = page;
                EvictIfNeeded();
            }
            
            // Ensure Slots list is initialized
            if (page.Slots == null)
            {
                page.Slots = new List<(ushort offset, ushort length, RecordFlags flags)>();
            }
            
            // Check space
            var requiredSpace = data.Length + SLOT_SIZE;
            if (page.FreeSpace < requiredSpace)
                throw new InvalidOperationException("Page full");
            
            // Insert record - calculate offset from start of data area
            var recordId = new RecordId(page.RecordCount);
            var dataSpan = page.Data.Span;
            
            // Calculate offset (grow from beginning of data area)
            ushort dataOffset = 0;
            if (page.Slots.Count > 0)
            {
                // Find the end of the last record
                var lastSlot = page.Slots[page.Slots.Count - 1];
                dataOffset = (ushort)(lastSlot.offset + lastSlot.length);
            }
            
            // Copy data
            data.CopyTo(dataSpan.Slice(dataOffset, data.Length));
            
            // Add slot metadata
            page.Slots.Add((dataOffset, (ushort)data.Length, RecordFlags.Active));
            
            // Update page metadata
            page.RecordCount++;
            page.FreeSpace -= (ushort)requiredSpace;
            page.IsDirty = true;
            
            pageCache[pageId.Value] = page;
            dirtyPages.Add(pageId.Value);
            UpdateLRU(pageId.Value);
            
            return recordId;
        }
    }
    
    public virtual void UpdateRecord(PageId pageId, RecordId recordId, byte[] newData)
    {
        if (pageId.Value == 0 || recordId.SlotIndex >= ushort.MaxValue)
            return;
            
        lock (writeLock)
        {
            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return;
                    
                pageCache[pageId.Value] = page;
            }
            
            if (recordId.SlotIndex >= page.Slots.Count)
                return;
            
            var slot = page.Slots[recordId.SlotIndex];
            
            // Simple in-place update (assumes same size for now)
            if (newData.Length <= slot.length)
            {
                var dataSpan = page.Data.Span;
                newData.CopyTo(dataSpan.Slice(slot.offset, newData.Length));
                
                // Update slot if size changed
                page.Slots[recordId.SlotIndex] = (slot.offset, (ushort)newData.Length, slot.flags);
                
                page.IsDirty = true;
                pageCache[pageId.Value] = page;
                dirtyPages.Add(pageId.Value);
            }
        }
    }
    
    public virtual void DeleteRecord(PageId pageId, RecordId recordId)
    {
        if (pageId.Value == 0 || recordId.SlotIndex >= ushort.MaxValue)
            return;
            
        lock (writeLock)
        {
            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return;
                    
                pageCache[pageId.Value] = page;
            }
            
            if (recordId.SlotIndex >= page.Slots.Count)
                return;
            
            var slot = page.Slots[recordId.SlotIndex];
            
            // Mark as deleted
            page.Slots[recordId.SlotIndex] = (slot.offset, slot.length, RecordFlags.Deleted);
            
            page.IsDirty = true;
            pageCache[pageId.Value] = page;
            dirtyPages.Add(pageId.Value);
        }
    }
    
    public virtual bool TryReadRecord(PageId pageId, RecordId recordId, out byte[]? data)
    {
        data = null;
        
        if (pageId.Value == 0 || recordId.SlotIndex >= ushort.MaxValue)
            return false;
            
        lock (writeLock)
        {
            // Get or load page
            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return false;
                    
                pageCache[pageId.Value] = page;
                UpdateLRU(pageId.Value);
                EvictIfNeeded();
            }
            else
            {
                UpdateLRU(pageId.Value);
            }
            
            // Ensure slots is initialized
            if (page.Slots == null || recordId.SlotIndex >= page.Slots.Count)
                return false;
            
            var slot = page.Slots[recordId.SlotIndex];
            
            // Check if record is deleted
            if (slot.flags.HasFlag(RecordFlags.Deleted))
                return false;
            
            // Read the data
            data = new byte[slot.length];
            page.Data.Span.Slice(slot.offset, slot.length).CopyTo(data);
            
            return true;
        }
    }
    
    public virtual IEnumerable<PageId> GetAllTablePages(uint tableId)
    {
        lock (writeLock)
        {
            if (tablePagesIndex.TryGetValue(tableId, out var pageIds))
            {
                return pageIds.Select(pid => new PageId(pid)).ToList();
            }
            return Enumerable.Empty<PageId>();
        }
    }
    
    public virtual IEnumerable<RecordId> GetAllRecordsInPage(PageId pageId)
    {
        lock (writeLock)
        {
            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return Enumerable.Empty<RecordId>();
            }
            
            var records = new List<RecordId>();
            for (ushort i = 0; i < page.Slots.Count; i++)
            {
                if (!page.Slots[i].flags.HasFlag(RecordFlags.Deleted))
                {
                    records.Add(new RecordId(i));
                }
            }
            return records;
        }
    }
    
    public virtual void FlushDirtyPages()
    {
        lock (writeLock)
        {
            foreach (var pageId in dirtyPages.ToList())
            {
                if (pageCache.TryGetValue(pageId, out var page))
                {
                    WritePage(new PageId(pageId), page);
                    page.IsDirty = false;
                    pageCache[pageId] = page;
                }
            }
            dirtyPages.Clear();
        }
    }
    
    public virtual Page? GetPage(PageId pageId, bool allowDirty = false)
    {
        if (pageId.Value == 0)
            return null;
            
        lock (writeLock)
        {
            // Check cache first
            if (pageCache.TryGetValue(pageId.Value, out var page))
            {
                cacheHits++;
                UpdateLRU(pageId.Value);
                
                if (!allowDirty && page.IsDirty)
                    return null;
                    
                return page;
            }
            
            // Cache miss - load from disk
            cacheMisses++;
            page = ReadPage(pageId);
            
            if (page.PageId == 0)
                return null;
            
            pageCache[pageId.Value] = page;
            UpdateLRU(pageId.Value);
            EvictIfNeeded();
            
            return page;
        }
    }
    
    public virtual (long Hits, long Misses, double HitRate, int Size, int Capacity) GetCacheStats()
    {
        lock (writeLock)
        {
            var total = cacheHits + cacheMisses;
            var hitRate = total > 0 ? (double)cacheHits / total : 0.0;
            return (cacheHits, cacheMisses, hitRate, pageCache.Count, cacheCapacity);
        }
    }
    
    public virtual void ResetCacheStats()
    {
        lock (writeLock)
        {
            cacheHits = 0;
            cacheMisses = 0;
        }
    }
    
    private void UpdateLRU(ulong pageId)
    {
        // Move to front (most recently used)
        if (lruNodeMap.TryGetValue(pageId, out var node))
        {
            lruList.Remove(node);
        }
        
        var newNode = lruList.AddFirst(pageId);
        lruNodeMap[pageId] = newNode;
    }
    
    private void EvictIfNeeded()
    {
        while (pageCache.Count > cacheCapacity)
        {
            // Evict least recently used
            if (lruList.Last != null)
            {
                var evictPageId = lruList.Last.Value;
                lruList.RemoveLast();
                lruNodeMap.Remove(evictPageId);
                
                // Flush if dirty
                if (dirtyPages.Contains(evictPageId))
                {
                    if (pageCache.TryGetValue(evictPageId, out var page))
                    {
                        WritePage(new PageId(evictPageId), page);
                    }
                    dirtyPages.Remove(evictPageId);
                }
                
                pageCache.Remove(evictPageId);
            }
        }
    }

    public virtual void Dispose()
    {
        pagesFile?.Dispose();
        GC.SuppressFinalize(this);
    }
}
