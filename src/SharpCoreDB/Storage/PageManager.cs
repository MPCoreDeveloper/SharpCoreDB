// <copyright file="PageManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

    // Page checksum (known issue §19): CRC32C of the page — computed with these four bytes left out — in header bytes 9–12,
    // and a format flag in byte 13. The header is 32 bytes and only bytes 0–8 were ever written, so a page written before
    // has flag 0 and is read unverified, as it always was; nothing else in the page moves.
    private const int ChecksumOffset = 9;
    private const int FormatFlagsOffset = 13;
    private const byte ChecksummedFlag = 0x01;
    protected const int MIN_RECORD_SIZE = 16;
    protected const int MAX_RECORD_SIZE = PAGE_SIZE - PAGE_HEADER_SIZE - SLOT_SIZE;

    // Storage fields
    protected FileStream? pagesFile;
    protected readonly Lock writeLock = new();
    
    // Cache implementation
    private readonly Dictionary<ulong, Page> pageCache = new();
    private readonly LinkedList<ulong> lruList = new();
    private readonly Dictionary<ulong, LinkedListNode<ulong>> lruNodeMap = new();
    private readonly HashSet<ulong> dirtyPages = new();
    private readonly int cacheCapacity;
    private ulong nextPageId = 1;

    // Track pages per table for FindPageWithSpace
    private readonly Dictionary<uint, List<ulong>> tablePagesIndex = new();

    // O(1) Free list implementation
    private readonly Queue<ulong> freePageList = new();

    // True while InitializeFromDisk scans every page header to rebuild the page index at open: a damaged page must not stop
    // the whole table from opening, so the checksum is verified when the page's records are read, not here.
    private bool skipChecksumVerification;

    // Transaction undo scope (see BeginUndoScope). Null outside a scope, so a write outside a transaction pays one null
    // check. Maps each page changed inside the scope to its image before the first change, or to null for a page the
    // scope allocated past the end of the file.
    private Dictionary<ulong, Page?>? undoImages;
    private Dictionary<uint, List<ulong>>? undoTablePages;
    private ulong[]? undoFreePages;
    
    /// <summary>Whether <see cref="FlushDirtyPages"/> syncs the page file to disk (WalDurabilityMode.FullSync).</summary>
    private readonly bool syncOnFlush = true;

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
        pagesFile = new FileStream(pagesFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        // ✅ CRITICAL FIX: Ensure file is physically created on disk
        pagesFile.Flush(flushToDisk: true);

        InitializeFromDisk();
    }

    public PageManager(string databasePath, uint tableId, DatabaseConfig? config)
    {
        cacheCapacity = config?.PageCacheCapacity ?? 1024;

        // FullSync (the default) forces flushed pages to the physical disk; Async writes them to the file and leaves the
        // sync to the operating system — the same split as the columnar in-place writes (known issue §17).
        syncOnFlush = (config?.WalDurabilityMode ?? Services.DurabilityMode.FullSync) == Services.DurabilityMode.FullSync;
        var pagesFilePath = Path.Combine(databasePath, $"table_{tableId}.pages");
        pagesFile = new FileStream(pagesFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        // ✅ CRITICAL FIX: Ensure file is physically created on disk
        // FileMode.OpenOrCreate doesn't guarantee file creation until first write
        // Force file creation by flushing immediately after opening
        pagesFile.Flush(flushToDisk: true);

        InitializeFromDisk();
    }

    /// <summary>
    /// Rebuilds in-memory page indexes from existing page file contents.
    /// Required for reopen scenarios so scans can discover persisted pages.
    /// </summary>
    private void InitializeFromDisk()
    {
        if (pagesFile is null)
        {
            return;
        }

        var length = pagesFile.Length;
        if (length < PAGE_SIZE)
        {
            nextPageId = 1;
            return;
        }

        var totalPages = (ulong)(length / PAGE_SIZE);
        nextPageId = totalPages + 1;

        tablePagesIndex.Clear();
        freePageList.Clear();

        skipChecksumVerification = true;
        try
        {
            ScanPageHeaders(totalPages);
        }
        finally
        {
            skipChecksumVerification = false;
        }
    }

    private void ScanPageHeaders(ulong totalPages)
    {
        for (ulong pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            var page = ReadPage(new PageId(pageNumber));
            if (page.PageId == 0)
            {
                continue;
            }

            if (page.Type == PageType.Free)
            {
                freePageList.Enqueue(pageNumber);
                continue;
            }

            if (!tablePagesIndex.TryGetValue(page.TableId, out var pages))
            {
                pages = [];
                tablePagesIndex[page.TableId] = pages;
            }

            pages.Add(pageNumber);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
            var buffer = ArrayPool<byte>.Shared.Rent(PAGE_SIZE);
            try
            {
                var bytesRead = pagesFile.Read(buffer, 0, PAGE_SIZE);

                if (bytesRead < PAGE_HEADER_SIZE)
                    return new();

                var bufferSpan = buffer.AsSpan();
                if (!skipChecksumVerification && bytesRead == PAGE_SIZE &&
                    (bufferSpan[FormatFlagsOffset] & ChecksummedFlag) != 0 &&
                    BinaryPrimitives.ReadUInt32LittleEndian(bufferSpan.Slice(ChecksumOffset)) != ComputePageChecksum(bufferSpan[..PAGE_SIZE]))
                {
                    // Up to 2.1.4 a torn page read as a page whose slots are empty: its rows vanished without an error and
                    // the table went on accepting writes on top of it (known issue §19).
                    throw new InvalidDataException(
                        $"PageBased page {pageId.Value} of '{pagesFile.Name}' failed its checksum: the page is damaged " +
                        "(for example by a write a crash or power loss interrupted). Restore the table from a backup.");
                }

                var page = new Page
                {
                    TableId = BinaryPrimitives.ReadUInt32LittleEndian(bufferSpan),
                    Type = (PageType)buffer[4],
                    RecordCount = BinaryPrimitives.ReadUInt16LittleEndian(bufferSpan.Slice(5)),
                    FreeSpace = BinaryPrimitives.ReadUInt16LittleEndian(bufferSpan.Slice(7)),
                    PageId = pageId.Value,
                    IsDirty = false,
                    Data = new Memory<byte>(new byte[PAGE_SIZE - PAGE_HEADER_SIZE]),
                    Slots = new List<(ushort offset, ushort length, RecordFlags flags)>()
                };

                // Copy data section
                bufferSpan.Slice(PAGE_HEADER_SIZE, PAGE_SIZE - PAGE_HEADER_SIZE).CopyTo(page.Data.Span);

                // Read slot metadata from the end of the page
                var slotAreaStart = PAGE_HEADER_SIZE + (PAGE_SIZE - PAGE_HEADER_SIZE) - (page.RecordCount * SLOT_SIZE);
                for (int i = 0; i < page.RecordCount; i++)
                {
                    var slotOffset = slotAreaStart + (i * SLOT_SIZE);
                    var offset16 = BinaryPrimitives.ReadUInt16LittleEndian(bufferSpan.Slice(slotOffset));
                    var length = BinaryPrimitives.ReadUInt16LittleEndian(bufferSpan.Slice(slotOffset + 2));

                    // A slot stored with length 0 is a deleted record (see WritePage).
                    page.Slots.Add((offset16, length, length == 0 ? RecordFlags.Deleted : RecordFlags.Active));
                }

                return page;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected virtual void WritePage(PageId pageId, Page page)
    {
        if (pagesFile == null || pageId.Value == 0)
            return;

        lock (writeLock)
        {
            var offset = (long)((pageId.Value - 1) * PAGE_SIZE);
            var buffer = ArrayPool<byte>.Shared.Rent(PAGE_SIZE);
            try
            {
                var bufferSpan = buffer.AsSpan(0, PAGE_SIZE);
                bufferSpan.Clear();

                // Write header using zero-alloc BinaryPrimitives
                BinaryPrimitives.WriteUInt32LittleEndian(bufferSpan, page.TableId);
                buffer[4] = (byte)page.Type;
                BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan.Slice(5), page.RecordCount);
                BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan.Slice(7), page.FreeSpace);

                // Write data
                page.Data.Span.CopyTo(bufferSpan.Slice(PAGE_HEADER_SIZE));

                // Write slot metadata
                var slotAreaStart = PAGE_HEADER_SIZE + (PAGE_SIZE - PAGE_HEADER_SIZE) - (page.RecordCount * SLOT_SIZE);
                for (int i = 0; i < page.Slots.Count && i < page.RecordCount; i++)
                {
                    var slotOffset = slotAreaStart + (i * SLOT_SIZE);
                    var slot = page.Slots[i];

                    // A slot holds only offset and length (SLOT_SIZE = 4), so a deleted record is stored with length 0 —
                    // a length no record has (a serialized row is never empty) — and ReadPage reads it back as deleted.
                    // Up to 2.1.4 the Deleted flag was never written and ReadPage marked every slot Active, so every
                    // PageBased DELETE was undone by a reopen: the deleted rows came back. A file written before keeps
                    // its old deleted slots' real lengths and still reads as it always did.
                    bool deleted = (slot.flags & RecordFlags.Deleted) == RecordFlags.Deleted;
                    BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan.Slice(slotOffset), slot.offset);
                    BinaryPrimitives.WriteUInt16LittleEndian(bufferSpan.Slice(slotOffset + 2), deleted ? (ushort)0 : slot.length);
                }

                bufferSpan[FormatFlagsOffset] = ChecksummedFlag;
                BinaryPrimitives.WriteUInt32LittleEndian(bufferSpan.Slice(ChecksumOffset), ComputePageChecksum(bufferSpan));

                pagesFile.Seek(offset, SeekOrigin.Begin);
                pagesFile.Write(buffer, 0, PAGE_SIZE);
                // PERF: Defer flushToDisk to FlushDirtyPages/Commit for batch write efficiency
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }
    }
    
    public virtual PageId AllocatePage(uint tableId, PageType pageType)
    {
        lock (writeLock)
        {
            // Check free list first (O(1) reuse)
            ulong pageIdValue;
            if (freePageList.Count > 0)
            {
                pageIdValue = freePageList.Dequeue();
                CaptureUndo(pageIdValue);
            }
            else
            {
                pageIdValue = nextPageId++;
                if (undoImages is not null)
                {
                    undoImages.TryAdd(pageIdValue, null);
                }
            }

            var pageId = new PageId(pageIdValue);
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

    /// <summary>
    /// Frees a page and adds it to the free list for O(1) reuse.
    /// /// <param name="pageId">Page ID to free.</param>
    public virtual void FreePage(PageId pageId)
    {
        if (pageId.Value == 0)
            return;

        lock (writeLock)
        {
            CaptureUndo(pageId.Value);

            // Add to free list for reuse
            freePageList.Enqueue(pageId.Value);

            // Remove from cache
            pageCache.Remove(pageId.Value);
            dirtyPages.Remove(pageId.Value);

            // Remove from LRU tracking
            if (lruNodeMap.TryGetValue(pageId.Value, out var node))
            {
                lruList.Remove(node);
                lruNodeMap.Remove(pageId.Value);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public virtual RecordId InsertRecord(PageId pageId, byte[] data)
    {
        if (pageId.Value == 0 || data.Length > MAX_RECORD_SIZE)
            return new(0);

        lock (writeLock)
        {
            CaptureUndo(pageId.Value);

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
            
            // Insert record - calculate offset from start of data area.
            // Records append at the end of the used data area. Using GetUsedDataEnd (max
            // active offset + length) rather than the last list slot is required once records
            // can be relocated within a page (a relocated record keeps its list position but
            // moves to a higher data offset).
            var recordId = new RecordId(page.RecordCount);
            var dataSpan = page.Data.Span;
            ushort dataOffset = (ushort)GetUsedDataEnd(page);
            
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
    
    /// <summary>
    /// Updates a record in place when it fits, or relocates it when it grows.
    /// - Same-size or smaller: overwrite in place (fast path, storage reference unchanged).
    /// - Larger with room in the same page: relocate within the page (slot pointer moves,
    ///   storage reference unchanged).
    /// - Larger with a full page: relocate to another page (old slot marked deleted;
    ///   a NEW RecordId is returned so callers can re-point indexes).
    /// </summary>
    /// <param name="pageId">The page containing the record.</param>
    /// <param name="recordId">The slot of the record to update.</param>
    /// <param name="newData">The new record bytes.</param>
    /// <returns>
    /// The record's current slot. Equal to <paramref name="recordId"/> when the storage
    /// reference is unchanged; a different slot when the record was relocated to another page.
    /// </returns>
    public virtual (PageId Page, RecordId Record) UpdateRecord(PageId pageId, RecordId recordId, byte[] newData)
    {
        if (pageId.Value == 0 || recordId.SlotIndex >= ushort.MaxValue || newData.Length > MAX_RECORD_SIZE)
            return (pageId, recordId);

        lock (writeLock)
        {
            CaptureUndo(pageId.Value);

            if (!pageCache.TryGetValue(pageId.Value, out var page))
            {
                page = ReadPage(pageId);
                if (page.PageId == 0)
                    return (pageId, recordId);

                pageCache[pageId.Value] = page;
            }

            if (recordId.SlotIndex >= page.Slots.Count)
                return (pageId, recordId);

            var slot = page.Slots[recordId.SlotIndex];
            var dataSpan = page.Data.Span;

            if (newData.Length <= slot.length)
            {
                // Same-size or smaller: in-place overwrite.
                newData.CopyTo(dataSpan.Slice(slot.offset, newData.Length));
                page.Slots[recordId.SlotIndex] = (slot.offset, (ushort)newData.Length, slot.flags);
                page.IsDirty = true;
                pageCache[pageId.Value] = page;
                dirtyPages.Add(pageId.Value);
                RecomputeFreeSpace(page);
                return (pageId, recordId);
            }

            // Growth: relocate within the same page when there is room at the end of the
            // used data area. The slot pointer moves, so the storage reference stays valid.
            int dataEnd = GetUsedDataEnd(page);
            int slotAreaSize = page.Slots.Count * SLOT_SIZE;
            int availableAtEnd = PAGE_SIZE - PAGE_HEADER_SIZE - slotAreaSize - dataEnd;

            if (newData.Length <= availableAtEnd)
            {
                newData.CopyTo(dataSpan.Slice(dataEnd, newData.Length));
                page.Slots[recordId.SlotIndex] = ((ushort)dataEnd, (ushort)newData.Length, slot.flags);
                page.IsDirty = true;
                pageCache[pageId.Value] = page;
                dirtyPages.Add(pageId.Value);
                RecomputeFreeSpace(page);
                return (pageId, recordId);
            }

            // Growth with a full page: mark the old slot deleted and relocate to another page.
            page.Slots[recordId.SlotIndex] = (slot.offset, slot.length, RecordFlags.Deleted);
            page.IsDirty = true;
            RecomputeFreeSpace(page);
            pageCache[pageId.Value] = page;
            dirtyPages.Add(pageId.Value);

            var newPageId = FindPageWithSpace(page.TableId, newData.Length);
            var newRecordId = InsertRecord(newPageId, newData);
            return (newPageId, newRecordId);
        }
    }

    /// <summary>
    /// Computes the end of the used data area (the highest byte offset of any active record).
    /// Records are packed from the start of the data area, so this is where new data appends.
    /// </summary>
    private static int GetUsedDataEnd(Page page)
    {
        // Plain indexed loop with a bitwise flag test.
        //
        // This runs on every PageBased UPDATE — RecomputeFreeSpace calls it, and the growth path calls it a
        // second time — across the page's entire slot list, so its per-slot cost sits on the hot path. The
        // previous form used LINQ (`Where` with a lambda, then `Math.Max` per element) plus
        // `RecordFlags.HasFlag`, and `Enum.HasFlag` boxes its receiver and its argument on every single call.
        // `(flags & Deleted) == Deleted` is exactly what HasFlag means for a non-zero flag, without the
        // allocation.
        //
        // Measured context (`--pk --engine=pagebased`): PageBased UPDATE 29,407 ops/s against 420,187 on the
        // append-only path — 10.3× vs SQLite there against 0.6× (ahead) here — so this loop is where a
        // PageBased UPDATE spends a suspicious amount of its time, twice over on the growth path.
        //
        // ⚠️ But this rewrite is NOT the fix, and the measurement says so: it moved PageBased UPDATE from
        // 29,407 to 36,208 ops/s plaintext and 27,697 to 28,842 at-rest, i.e. inside this machine's ±20 %
        // band. It is kept because removing a boxed `HasFlag` and a LINQ delegate per slot cannot be worse,
        // not because it is a win. The dominant PageBased UPDATE cost is still unidentified — the next step
        // is to profile that path rather than guess at it again.
        int dataEnd = 0;
        var slots = page.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if ((slot.flags & RecordFlags.Deleted) == RecordFlags.Deleted)
            {
                continue; // a deleted record does not extend the used data area
            }

            int end = slot.offset + slot.length;
            if (end > dataEnd)
            {
                dataEnd = end;
            }
        }

        return dataEnd;
    }

    /// <summary>
    /// Recomputes the contiguous free space at the end of the page's data area.
    /// The append-only allocator can only use contiguous space, so FreeSpace must never
    /// include middle-of-page holes left by shrunk, deleted, or relocated records.
    /// </summary>
    private static void RecomputeFreeSpace(Page page)
    {
        int slotAreaSize = page.Slots.Count * SLOT_SIZE;
        int dataEnd = GetUsedDataEnd(page);
        int free = PAGE_SIZE - PAGE_HEADER_SIZE - slotAreaSize - dataEnd;
        page.FreeSpace = (ushort)Math.Max(0, free);
    }
    
    public virtual void DeleteRecord(PageId pageId, RecordId recordId)
    {
        if (pageId.Value == 0 || recordId.SlotIndex >= ushort.MaxValue)
            return;

        lock (writeLock)
        {
            CaptureUndo(pageId.Value);

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
    
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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
            // Nothing to write: no fsync either. The flush now runs after every autocommit statement (known issue §15),
            // and a SELECT must not pay a disk sync for it.
            if (dirtyPages.Count == 0)
            {
                return;
            }

            // A page the open transaction changed stays dirty and in memory: a flush in the middle of a transaction (an
            // ExecuteQuery inside BeginBatchUpdate flushes, to make batched writes visible) wrote it to the file, and a
            // crash before the commit or rollback kept the uncommitted change (known issue §18). The commit flushes it.
            foreach (var pageId in dirtyPages.ToList())
            {
                if (IsPinned(pageId))
                {
                    continue;
                }

                if (pageCache.TryGetValue(pageId, out var page))
                {
                    WritePage(new PageId(pageId), page);
                    page.IsDirty = false;
                    pageCache[pageId] = page;
                }

                dirtyPages.Remove(pageId);
            }

            // PERF: Single OS-level flush after all pages written (a disk sync under FullSync only)
            pagesFile?.Flush(flushToDisk: syncOnFlush);
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
        // Least recently used first. A dirty page the open transaction changed is pinned: evicting it would write an
        // uncommitted change to the file, and a crash before the commit or rollback kept it (known issue §18 — measured:
        // an uncommitted UPDATE and DELETE survived a crash after a large read). The cache may then exceed its capacity
        // until the transaction ends. The walk also ends when the list is exhausted; the previous loop spun forever when
        // the cache held more pages than its capacity and none of them was in the LRU list.
        var node = lruList.Last;
        while (pageCache.Count > cacheCapacity && node is not null)
        {
            var previous = node.Previous;
            var candidate = node.Value;
            if (!IsPinned(candidate))
            {
                lruList.Remove(node);
                lruNodeMap.Remove(candidate);
                if (dirtyPages.Remove(candidate) && pageCache.TryGetValue(candidate, out var page))
                {
                    WritePage(new PageId(candidate), page);
                }

                pageCache.Remove(candidate);
            }

            node = previous;
        }
    }

    /// <summary>True for a dirty page changed inside the open undo scope: it must not reach the file before the commit.</summary>
    private bool IsPinned(ulong pageId) =>
        undoImages is not null && undoImages.ContainsKey(pageId) && dirtyPages.Contains(pageId);

    /// <summary>
    /// CRC32C of an 8 KB page with its checksum bytes (header 9–12) left out, using the CPU's CRC32 instruction where there
    /// is one (<see cref="System.Numerics.BitOperations.Crc32C(uint, ulong)"/> falls back to software elsewhere).
    /// </summary>
    internal static uint ComputePageChecksum(ReadOnlySpan<byte> page)
    {
        uint crc = Crc32C(uint.MaxValue, page[..ChecksumOffset]);
        crc = Crc32C(crc, page[(ChecksumOffset + 4)..]);
        return ~crc;
    }

    private static uint Crc32C(uint crc, ReadOnlySpan<byte> data)
    {
        int i = 0;
        for (; i + 8 <= data.Length; i += 8)
        {
            crc = System.Numerics.BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i)));
        }

        for (; i < data.Length; i++)
        {
            crc = System.Numerics.BitOperations.Crc32C(crc, data[i]);
        }

        return crc;
    }

    /// <summary>
    /// Starts a transaction undo scope: from here on, the first change to each page keeps a copy of the page as it was,
    /// so <see cref="RollbackUndoScope"/> can put it back. Up to 2.1.4 the PageBased engine had no rollback at all — it
    /// writes into this cache and the page file directly, outside the storage transaction buffer — so a rolled-back
    /// UPDATE or INSERT stayed, also after a reopen (known issue §13). Starting a scope that is already open is a no-op.
    /// </summary>
    public void BeginUndoScope()
    {
        lock (writeLock)
        {
            if (undoImages is not null)
            {
                return;
            }

            undoImages = new Dictionary<ulong, Page?>();
            undoTablePages = tablePagesIndex.ToDictionary(kv => kv.Key, kv => new List<ulong>(kv.Value));
            undoFreePages = [.. freePageList];
        }
    }

    /// <summary>Ends the undo scope and keeps every change (commit).</summary>
    public void CommitUndoScope()
    {
        lock (writeLock)
        {
            undoImages = null;
            undoTablePages = null;
            undoFreePages = null;
        }
    }

    /// <summary>
    /// Ends the undo scope and puts every page changed inside it back as it was (rollback). Restored pages are marked
    /// dirty, so a page the cache had already written to the file during the scope is written back on the next flush; a
    /// page the scope allocated is written as a free page and returned to the free list.
    /// </summary>
    /// <returns><c>true</c> when the scope changed any page.</returns>
    public bool RollbackUndoScope()
    {
        lock (writeLock)
        {
            if (undoImages is null)
            {
                return false;
            }

            var images = undoImages;
            undoImages = null;

            tablePagesIndex.Clear();
            foreach (var (tableId, pages) in undoTablePages!)
            {
                tablePagesIndex[tableId] = pages;
            }

            freePageList.Clear();
            foreach (var free in undoFreePages!)
            {
                freePageList.Enqueue(free);
            }

            undoTablePages = null;
            undoFreePages = null;

            foreach (var (pageId, image) in images)
            {
                var restored = image ?? new Page
                {
                    TableId = 0,
                    Type = PageType.Free,
                    RecordCount = 0,
                    FreeSpace = (ushort)(PAGE_SIZE - PAGE_HEADER_SIZE),
                    PageId = pageId,
                    Data = new Memory<byte>(new byte[PAGE_SIZE - PAGE_HEADER_SIZE]),
                    Slots = [],
                };

                if (image is null)
                {
                    freePageList.Enqueue(pageId);
                }

                restored = ClonePage(restored);
                restored.IsDirty = true;
                pageCache[pageId] = restored;
                UpdateLRU(pageId);
                dirtyPages.Add(pageId);
            }

            EvictIfNeeded();
            return images.Count > 0;
        }
    }

    /// <summary>Keeps the page's image before its first change inside an undo scope. Called under the write lock.</summary>
    private void CaptureUndo(ulong pageId)
    {
        if (undoImages is null || undoImages.ContainsKey(pageId))
        {
            return;
        }

        if (pageCache.TryGetValue(pageId, out var cached))
        {
            undoImages[pageId] = ClonePage(cached);
            return;
        }

        var onDisk = ReadPage(new PageId(pageId));
        undoImages[pageId] = onDisk.PageId == 0 ? null : ClonePage(onDisk);
    }

    private static Page ClonePage(Page page) => page with
    {
        Data = new Memory<byte>(page.Data.ToArray()),
        Slots = page.Slots is null ? [] : [.. page.Slots],
    };

    public virtual void Dispose()
    {
        pagesFile?.Dispose();
        GC.SuppressFinalize(this);
    }
}
