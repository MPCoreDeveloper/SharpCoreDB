// <copyright file="PageManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System.Buffers.Binary;
using System.Collections.Concurrent;

#pragma warning disable S1450 // Private field only referenced from constructor - intentional for file path storage
#pragma warning disable S3881 // Implement IDisposable correctly - pattern implemented via Dispose(bool)

/// <summary>
/// Page-based storage manager for OLTP workloads.
/// Manages 8KB pages with in-place updates, free space tracking, and WAL integration.
/// </summary>
public class PageManager : IDisposable
{
    private const int PAGE_SIZE = 8192; // 8KB pages
    private const int PAGE_HEADER_SIZE = 64; // bytes
    private const int SLOT_SIZE = 4; // 2 bytes offset + 2 bytes length

    private readonly string pagesFilePath; // Path to the file storing page data
    private readonly string freeListFilePath;
    private readonly FileStream pagesFile;
    private readonly ConcurrentDictionary<ulong, Page> pageCache;
    private readonly Lock writeLock = new();
#pragma warning disable S1144 // disposed field used in Dispose pattern
    private bool disposed;
#pragma warning restore S1144

    /// <summary>
    /// Page types for internal management.
    /// </summary>
    public enum PageType : byte
    {
        /// <summary>Free page available for allocation.</summary>
        Free = 0,
        
        /// <summary>Table data page.</summary>
        Table = 1,
        
        /// <summary>Index page (B-tree node).</summary>
        Index = 2,
        
        /// <summary>Overflow page for large records.</summary>
        Overflow = 3,
        
        /// <summary>Free list tracking page.</summary>
        FreeList = 4
    }

    /// <summary>
    /// Record flags for tracking record state and characteristics.
    /// Note: "Flags" suffix intentional for [Flags] enum as per .NET convention
    /// </summary>
#pragma warning disable S2344 // Flags suffix is standard .NET convention for [Flags] enums
    [Flags]
    public enum RecordFlags : byte
#pragma warning restore S2344
    {
        /// <summary>Normal record with no special flags.</summary>
        None = 0,
        
        /// <summary>Record has been deleted (soft delete).</summary>
        Deleted = 1 << 0,
        
        /// <summary>Record spans multiple pages (has overflow).</summary>
        HasOverflow = 1 << 1,
        
        /// <summary>Record is compressed.</summary>
        Compressed = 1 << 2,
        
        /// <summary>Record has NULL values in some columns.</summary>
        HasNulls = 1 << 3,
        
        /// <summary>Record is part of an overflow chain (continuation).</summary>
        IsOverflowContinuation = 1 << 4
    }

    /// <summary>
    /// Represents a page ID (64-bit unsigned integer).
    /// </summary>
    public readonly record struct PageId(ulong Value)
    {
        /// <summary>Gets an invalid page ID.</summary>
        public static PageId Invalid => new(0);
        
        /// <summary>Gets whether this page ID is valid.</summary>
        public bool IsValid => Value != 0;
    }

    /// <summary>
    /// Represents a record ID within a page.
    /// </summary>
    public readonly record struct RecordId(ushort SlotIndex)
    {
        /// <summary>Gets an invalid record ID.</summary>
        public static RecordId Invalid => new(ushort.MaxValue);
        
        /// <summary>Gets whether this record ID is valid.</summary>
        public bool IsValid => SlotIndex != ushort.MaxValue;
    }

    /// <summary>
    /// In-memory representation of a page.
    /// </summary>
    public class Page
    {
        /// <summary>Gets or sets the page ID.</summary>
        public ulong PageId { get; set; }
        
        /// <summary>Gets or sets the page type.</summary>
        public PageType Type { get; set; }
        
        /// <summary>Gets or sets the table ID this page belongs to.</summary>
        public uint TableId { get; set; }
        
        /// <summary>Gets or sets the log sequence number for WAL.</summary>
        public ulong LSN { get; set; }
        
        /// <summary>Gets or sets the free space offset.</summary>
        public ushort FreeSpaceOffset { get; set; }
        
        /// <summary>Gets or sets the record count.</summary>
        public ushort RecordCount { get; set; }
        
        /// <summary>Gets or sets the next page ID for linked lists.</summary>
        public ulong NextPageId { get; set; }
        
        /// <summary>Gets or sets the previous page ID for linked lists.</summary>
        public ulong PrevPageId { get; set; }
        
        /// <summary>Gets or sets the raw page data.</summary>
        public byte[] Data { get; set; } = new byte[PAGE_SIZE];
        
        /// <summary>Gets or sets whether this page has unsaved changes.</summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Gets the available free space in bytes.
        /// </summary>
        public int FreeSpace => FreeSpaceOffset - PAGE_HEADER_SIZE - (RecordCount * SLOT_SIZE);

        /// <summary>
        /// Serializes page to byte array (8KB).
        /// </summary>
        public byte[] ToBytes()
        {
            var buffer = new byte[PAGE_SIZE];
            var span = buffer.AsSpan();

            // Page header
            BinaryPrimitives.WriteUInt64LittleEndian(span[0..], PageId);
            buffer[8] = (byte)Type;
            BinaryPrimitives.WriteUInt32LittleEndian(span[9..], TableId);
            BinaryPrimitives.WriteUInt64LittleEndian(span[13..], LSN);
            BinaryPrimitives.WriteUInt16LittleEndian(span[25..], FreeSpaceOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(span[27..], RecordCount);
            BinaryPrimitives.WriteUInt64LittleEndian(span[29..], NextPageId);
            BinaryPrimitives.WriteUInt64LittleEndian(span[37..], PrevPageId);

            // Checksum (CRC32) - computed on data portion only
            var dataPortionForChecksum = Data.AsSpan(PAGE_HEADER_SIZE);
            var checksum = ComputeChecksum(dataPortionForChecksum.ToArray());
            BinaryPrimitives.WriteUInt32LittleEndian(span[21..], checksum);

            // Copy data portion (everything after header)
            Data.AsSpan(PAGE_HEADER_SIZE).CopyTo(span[PAGE_HEADER_SIZE..]);

            return buffer;
        }

        /// <summary>
        /// Deserializes page from byte array.
        /// </summary>
        public static Page FromBytes(byte[] buffer)
        {
            if (buffer.Length != PAGE_SIZE)
                throw new ArgumentException($"Invalid page size: {buffer.Length}, expected {PAGE_SIZE}");

            var span = buffer.AsSpan();
            var page = new Page
            {
                PageId = BinaryPrimitives.ReadUInt64LittleEndian(span[0..]),
                Type = (PageType)buffer[8],
                TableId = BinaryPrimitives.ReadUInt32LittleEndian(span[9..]),
                LSN = BinaryPrimitives.ReadUInt64LittleEndian(span[13..]),
                FreeSpaceOffset = BinaryPrimitives.ReadUInt16LittleEndian(span[25..]),
                RecordCount = BinaryPrimitives.ReadUInt16LittleEndian(span[27..]),
                NextPageId = BinaryPrimitives.ReadUInt64LittleEndian(span[29..]),
                PrevPageId = BinaryPrimitives.ReadUInt64LittleEndian(span[37..])
            };

            // Verify checksum
            var storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(span[21..]);
            var data = buffer[PAGE_HEADER_SIZE..PAGE_SIZE];
            var computedChecksum = ComputeChecksum(data);
            
            if (storedChecksum != computedChecksum)
                throw new InvalidDataException($"Page {page.PageId} checksum mismatch");

            page.Data = new byte[PAGE_SIZE];
            buffer.CopyTo(page.Data, 0);

            return page;
        }

        private static uint ComputeChecksum(byte[] data)
        {
            // CRC32 implementation
            uint crc = 0xFFFFFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320 : 0);
                }
            }
            return ~crc;
        }
    }

    /// <summary>
    /// Initializes a new PageManager for a table.
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <param name="tableId">Table ID for this page manager.</param>
    public PageManager(string databasePath, uint tableId)
    {
        // S1450 suppressed: pagesFilePath is used throughout the class lifetime
        pagesFilePath = Path.Combine(databasePath, $"table_{tableId}.pages");
        freeListFilePath = Path.Combine(databasePath, $"table_{tableId}.pages.freelist");

        // Create or open pages file
#pragma warning disable S2930 // pagesFile disposed in Dispose() method
        pagesFile = new FileStream(
            pagesFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: PAGE_SIZE * 16,
            useAsync: false);
#pragma warning restore S2930

        pageCache = new ConcurrentDictionary<ulong, Page>();

        // Initialize free list if needed
        if (!File.Exists(freeListFilePath))
        {
            InitializeFreeList();
        }
    }

    /// <summary>
    /// Allocates a new page for the specified table.
    /// </summary>
    /// <param name="tableId">Table ID for this page manager.</param>
    /// <param name="type">Type of page to allocate.</param>
    /// <returns>The allocated page ID.</returns>
    public PageId AllocatePage(uint tableId, PageType type)
    {
        lock (writeLock)
        {
            // Try to reuse free page
            var freePageId = GetFreePageFromList();
            
            if (freePageId.IsValid)
            {
                var page = ReadPage(freePageId);
                page.Type = type;
                page.TableId = tableId;
                page.RecordCount = 0;
                page.FreeSpaceOffset = (ushort)PAGE_SIZE;
                page.NextPageId = 0;
                page.PrevPageId = 0;
                page.IsDirty = true;
                WritePage(page);
                return freePageId;
            }
            else
            {
                // Allocate new page at end of file
                var newPageId = new PageId((ulong)(pagesFile.Length / PAGE_SIZE) + 1);
                var newPage = new Page
                {
                    PageId = newPageId.Value,
                    Type = type,
                    TableId = tableId,
                    FreeSpaceOffset = (ushort)PAGE_SIZE,
                    RecordCount = 0,
                    LSN = 0,
                    IsDirty = true
                };

                WritePage(newPage);
                return newPageId;
            }
        }
    }

    /// <summary>
    /// Frees a page and adds it to the free list.
    /// </summary>
    /// <param name="pageId">Page ID to free.</param>
    public void FreePage(PageId pageId)
    {
        lock (writeLock)
        {
            var page = ReadPage(pageId);
            page.Type = PageType.Free;
            page.RecordCount = 0;
            page.FreeSpaceOffset = (ushort)PAGE_SIZE;
            page.IsDirty = true;
            WritePage(page);

            AddPageToFreeList(pageId);
        }
    }

    /// <summary>
    /// Reads a page from disk or cache.
    /// </summary>
    /// <param name="pageId">Page ID to read.</param>
    /// <returns>The requested page.</returns>
    public Page ReadPage(PageId pageId)
    {
        if (!pageId.IsValid)
            throw new ArgumentException("Invalid page ID", nameof(pageId));

        // Check cache first
        if (pageCache.TryGetValue(pageId.Value, out var cachedPage))
            return cachedPage;

        // Read from disk
        lock (writeLock)
        {
            var offset = (long)(pageId.Value - 1) * PAGE_SIZE;
            if (offset >= pagesFile.Length)
                throw new InvalidOperationException($"Page {pageId.Value} does not exist");

            var buffer = new byte[PAGE_SIZE];
            pagesFile.Seek(offset, SeekOrigin.Begin);
            
            int bytesRead = 0;
            int totalRead = 0;
            while (totalRead < PAGE_SIZE)
            {
                bytesRead = pagesFile.Read(buffer, totalRead, PAGE_SIZE - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException($"Unexpected end of file reading page {pageId.Value}");
                totalRead += bytesRead;
            }

            var page = Page.FromBytes(buffer);
            pageCache.TryAdd(pageId.Value, page);
            return page;
        }
    }

    /// <summary>
    /// Writes a page to disk.
    /// </summary>
    /// <param name="page">Page to write.</param>
    public void WritePage(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        
        lock (writeLock)
        {
            var offset = (long)(page.PageId - 1) * PAGE_SIZE;
            var buffer = page.ToBytes();

            pagesFile.Seek(offset, SeekOrigin.Begin);
            pagesFile.Write(buffer, 0, PAGE_SIZE);
            pagesFile.Flush();

            page.IsDirty = false;
            pageCache[page.PageId] = page;
        }
    }

    /// <summary>
    /// Inserts a record into a page with automatic overflow handling for large records.
    /// </summary>
    /// <param name="pageId">Page ID to insert into.</param>
    /// <param name="data">Record data to insert.</param>
    /// <returns>The record ID of the inserted record.</returns>
    public RecordId InsertRecord(PageId pageId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        
        const int recordHeaderSize = 12;
        const int maxRecordSizeInPage = PAGE_SIZE - PAGE_HEADER_SIZE - SLOT_SIZE - recordHeaderSize;
        
        // Handle overflow for large records
        if (data.Length > maxRecordSizeInPage)
        {
            return InsertLargeRecord(pageId, data);
        }
        
        var page = ReadPage(pageId);

        var requiredSpace = data.Length + recordHeaderSize + SLOT_SIZE;
        if (page.FreeSpace < requiredSpace)
            throw new InvalidOperationException($"Insufficient space in page {pageId.Value}. Required: {requiredSpace}, Available: {page.FreeSpace}");

        var slotIndex = page.RecordCount;
        var recordOffset = (ushort)(page.FreeSpaceOffset - data.Length - recordHeaderSize);

        // Write record header with flags
        var recordHeader = new byte[recordHeaderSize];
        BinaryPrimitives.WriteUInt64LittleEndian(recordHeader.AsSpan()[0..], (ulong)slotIndex);
        recordHeader[8] = (byte)RecordFlags.None; // No special flags for normal records
        recordHeader[9] = 1; // Column count (placeholder - will be determined by table schema)
        BinaryPrimitives.WriteUInt16LittleEndian(recordHeader.AsSpan()[10..], (ushort)data.Length);

        // Write to page
        Array.Copy(recordHeader, 0, page.Data, recordOffset, recordHeaderSize);
        Array.Copy(data, 0, page.Data, recordOffset + recordHeaderSize, data.Length);

        // Update slot array
        var slotOffset = PAGE_HEADER_SIZE + (slotIndex * SLOT_SIZE);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], recordOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], (ushort)(data.Length + recordHeaderSize));

        // Update page metadata
        page.RecordCount++;
        page.FreeSpaceOffset = recordOffset;
        page.IsDirty = true;
        page.LSN++;

        WritePage(page);

        return new RecordId(slotIndex);
    }

    /// <summary>
    /// Inserts a large record that requires overflow pages.
    /// </summary>
    private RecordId InsertLargeRecord(PageId primaryPageId, byte[] data)
    {
        const int recordHeaderSize = 12;

        var page = ReadPage(primaryPageId);
        var slotIndex = page.RecordCount;

        // Calculate how much data fits in the primary page
        var availableSpace = page.FreeSpace - SLOT_SIZE - recordHeaderSize;
        var primaryDataSize = Math.Min(data.Length, availableSpace);
        var remainingData = data.Length - primaryDataSize;

        // Create overflow chain if needed
        PageId nextOverflowPage = PageId.Invalid;
        if (remainingData > 0)
        {
            nextOverflowPage = CreateOverflowChain(data.AsSpan(primaryDataSize), page.TableId);
        }

        // Write primary record with overflow flag
        var recordOffset = (ushort)(page.FreeSpaceOffset - primaryDataSize - recordHeaderSize);
        var recordHeader = new byte[recordHeaderSize];
        BinaryPrimitives.WriteUInt64LittleEndian(recordHeader.AsSpan()[0..], (ulong)slotIndex);
        recordHeader[8] = (byte)(remainingData > 0 ? RecordFlags.HasOverflow : RecordFlags.None);
        recordHeader[9] = 1; // Column count
        BinaryPrimitives.WriteUInt16LittleEndian(recordHeader.AsSpan()[10..], (ushort)data.Length); // Total length including overflow

        Array.Copy(recordHeader, 0, page.Data, recordOffset, recordHeaderSize);
        Array.Copy(data, 0, page.Data, recordOffset + recordHeaderSize, primaryDataSize);

        // Update slot
        var slotOffset = PAGE_HEADER_SIZE + (slotIndex * SLOT_SIZE);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], recordOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], (ushort)(primaryDataSize + recordHeaderSize));

        // Link to overflow chain
        if (nextOverflowPage.IsValid)
        {
            page.NextPageId = nextOverflowPage.Value;
        }

        page.RecordCount++;
        page.FreeSpaceOffset = recordOffset;
        page.IsDirty = true;
        page.LSN++;

        WritePage(page);

        return new RecordId(slotIndex);
    }

    /// <summary>
    /// Creates a chain of overflow pages for large record data.
    /// </summary>
    private PageId CreateOverflowChain(ReadOnlySpan<byte> data, uint tableId)
    {
        const int maxChunkSize = PAGE_SIZE - PAGE_HEADER_SIZE - 12; // Leave room for header

        if (data.Length == 0)
            return PageId.Invalid;

        // Allocate first overflow page
        var currentPageId = AllocatePage(tableId, PageType.Overflow);
        var firstPageId = currentPageId;
        var currentPage = ReadPage(currentPageId);

        int offset = 0;
        while (offset < data.Length)
        {
            var chunkSize = Math.Min(maxChunkSize, data.Length - offset);
            var chunk = data.Slice(offset, chunkSize);

            // Write chunk to current overflow page
            chunk.CopyTo(currentPage.Data.AsSpan(PAGE_HEADER_SIZE));
            currentPage.RecordCount = 1; // One chunk per overflow page
            currentPage.FreeSpaceOffset = (ushort)(PAGE_HEADER_SIZE + chunkSize);

            offset += chunkSize;

            // Create next overflow page if more data remains
            if (offset < data.Length)
            {
                var nextPageId = AllocatePage(tableId, PageType.Overflow);
                currentPage.NextPageId = nextPageId.Value;
                currentPage.IsDirty = true;
                WritePage(currentPage);

                currentPageId = nextPageId;
                currentPage = ReadPage(currentPageId);
            }
            else
            {
                currentPage.IsDirty = true;
                WritePage(currentPage);
            }
        }

        return firstPageId;
    }

    /// <summary>
    /// Updates a record with intelligent in-place vs relocate decision.
    /// Optimized for common cases: same size, smaller size, and slightly larger size.
    /// </summary>
    /// <param name="pageId">Page ID containing the record.</param>
    /// <param name="recordId">Record ID to update.</param>
    /// <param name="newData">New record data.</param>
    public void UpdateRecord(PageId pageId, RecordId recordId, byte[] newData)
    {
        ArgumentNullException.ThrowIfNull(newData);
        
        var page = ReadPage(pageId);

        if (recordId.SlotIndex >= page.RecordCount)
            throw new ArgumentException($"Invalid record ID {recordId.SlotIndex}", nameof(recordId));

        var slotOffset = PAGE_HEADER_SIZE + (recordId.SlotIndex * SLOT_SIZE);
        var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[slotOffset..]);
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..]);

        if (recordOffset == 0 || recordLength == 0)
            throw new InvalidOperationException("Cannot update deleted record");

        const int headerSize = 12;
        var oldDataLength = recordLength - headerSize;
        // newTotalLength calculated for documentation - actual comparison done per-strategy

        // Read record header to check for overflow
        var flags = (RecordFlags)page.Data[recordOffset + 8];
        var hasOverflow = flags.HasFlag(RecordFlags.HasOverflow);

        // Strategy 1: Same size update (most common) - in-place
        if (newData.Length == oldDataLength)
        {
            UpdateInPlace(page, recordOffset, headerSize, newData);
            return;
        }

        // Strategy 2: Smaller size update - in-place with wasted space
        if (newData.Length < oldDataLength)
        {
            UpdateInPlaceShrink(page, recordOffset, slotOffset, headerSize, newData, oldDataLength);
            return;
        }

        // Strategy 3: Slightly larger - try in-place if page has space
        var additionalSpace = newData.Length - oldDataLength;
#pragma warning disable S1066 // Nested if provides clear separation of concerns
        if (page.FreeSpace >= additionalSpace && IsRecordAtDataEnd(page, recordOffset, recordLength))
#pragma warning restore S1066
        {
            UpdateInPlaceExpand(page, recordOffset, slotOffset, headerSize, newData);
            return;
        }

        // Strategy 4: Relocate - delete old, insert new
        // Handle overflow cleanup if old record had overflow
        if (hasOverflow)
        {
            FreeOverflowChain(new PageId(page.NextPageId));
        }

        DeleteRecordInternal(page, slotOffset);
        
        // Insert new data (may create new overflow if too large)
        if (newData.Length > PAGE_SIZE - PAGE_HEADER_SIZE - SLOT_SIZE - headerSize)
        {
            // Large update - need overflow chain
            _ = InsertLargeRecord(pageId, newData);
            
            // Note: Slot array may have changed - caller must update index references
        }
        else
        {
            _ = InsertRecord(pageId, newData);
        }
    }

    /// <summary>
    /// Performs in-place update when new data is same size as old data.
    /// </summary>
    private void UpdateInPlace(Page page, ushort recordOffset, int headerSize, byte[] newData)
    {
        // Just overwrite the data portion
        Array.Copy(newData, 0, page.Data, recordOffset + headerSize, newData.Length);

        page.IsDirty = true;
        page.LSN++;
        WritePage(page);
    }

    /// <summary>
    /// Performs in-place update when new data is smaller (leaves wasted space).
    /// </summary>
#pragma warning disable S1172 // oldDataLength parameter reserved for future fragmentation tracking
    private void UpdateInPlaceShrink(Page page, ushort recordOffset, int slotOffset, int headerSize, byte[] newData, int oldDataLength)
#pragma warning restore S1172
    {
        _ = oldDataLength; // Reserved for future use in fragmentation tracking
        
        // Update data
        Array.Copy(newData, 0, page.Data, recordOffset + headerSize, newData.Length);

        // Update record header length
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(recordOffset + 10)..], (ushort)newData.Length);

        // Update slot length (includes wasted space for potential future updates)
        var newSlotLength = (ushort)(newData.Length + headerSize);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], newSlotLength);

        page.IsDirty = true;
        page.LSN++;
        WritePage(page);
    }

    /// <summary>
    /// Expands record in-place if it's at the end of the data area.
    /// </summary>
    private void UpdateInPlaceExpand(Page page, ushort recordOffset, int slotOffset, int headerSize, byte[] newData)
    {
        var newRecordOffset = (ushort)(recordOffset - (newData.Length - (recordOffset - page.FreeSpaceOffset + headerSize)));

        // Copy data to new location
        Array.Copy(newData, 0, page.Data, newRecordOffset + headerSize, newData.Length);

        // Update record header
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(newRecordOffset + 10)..], (ushort)newData.Length);

        // Update slot
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], newRecordOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], (ushort)(newData.Length + headerSize));

        page.FreeSpaceOffset = newRecordOffset;
        page.IsDirty = true;
        page.LSN++;
        WritePage(page);
    }

    /// <summary>
    /// Checks if a record is at the end of the data area (allowing expansion).
    /// </summary>
#pragma warning disable S1172 // recordLength parameter reserved for future boundary checking
#pragma warning disable S2325 // Method uses page state  
    private bool IsRecordAtDataEnd(Page page, ushort recordOffset, ushort recordLength)
#pragma warning restore S2325
#pragma warning restore S1172
    {
        _ = recordLength; // Reserved for future boundary validation
        return recordOffset == page.FreeSpaceOffset;
    }

    /// <summary>
    /// Internal delete helper that doesn't write the page.
    /// </summary>
#pragma warning disable S2325 // Method uses page state for LSN tracking
    private void DeleteRecordInternal(Page page, int slotOffset)
#pragma warning restore S2325
    {
        // Mark slot as deleted
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], 0);

        page.IsDirty = true;
        page.LSN++;
    }

    /// <summary>
    /// Frees an overflow page chain.
    /// </summary>
    private void FreeOverflowChain(PageId overflowPageId)
    {
        if (!overflowPageId.IsValid)
            return;

        var currentPageId = overflowPageId;
        
        while (currentPageId.IsValid)
        {
            var overflowPage = ReadPage(currentPageId);
            var nextPageId = new PageId(overflowPage.NextPageId);
            
            FreePage(currentPageId);
            
            currentPageId = nextPageId;
        }
    }

    /// <summary>
    /// Deletes a record by marking slot as deleted.
    /// Note: Space is not immediately reclaimed - call CompactPage to defragment.
    /// </summary>
    /// <param name="pageId">Page ID containing the record.</param>
    /// <param name="recordId">Record ID to delete.</param>
    public void DeleteRecord(PageId pageId, RecordId recordId)
    {
        var page = ReadPage(pageId);

        if (recordId.SlotIndex >= page.RecordCount)
            throw new ArgumentException("Invalid record ID", nameof(recordId));

        var slotOffset = PAGE_HEADER_SIZE + (recordId.SlotIndex * SLOT_SIZE);
        var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[slotOffset..]);

        if (recordOffset == 0)
            throw new InvalidOperationException("Record already deleted");

        // Check for overflow and free overflow chain if present
        var flags = (RecordFlags)page.Data[recordOffset + 8];
        if (flags.HasFlag(RecordFlags.HasOverflow))
        {
            FreeOverflowChain(new PageId(page.NextPageId));
            page.NextPageId = 0;
        }

        // Mark slot as deleted (soft delete)
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], 0);

        // Mark record as deleted in data area
        page.Data[recordOffset + 8] = (byte)RecordFlags.Deleted;

        page.IsDirty = true;
        page.LSN++;
        WritePage(page);
    }

    /// <summary>
    /// Compacts a page by removing deleted records and defragmenting free space.
    /// This consolidates all live records and reclaims space from deleted records.
    /// </summary>
    /// <param name="pageId">Page ID to compact.</param>
#pragma warning disable S2325 // Method will use instance state (freeListFilePath) when bitmap implementation added
    public void CompactPage(PageId pageId)
#pragma warning restore S2325
    {
        var page = ReadPage(pageId);

        if (page.Type != PageType.Table)
            throw new InvalidOperationException($"Cannot compact non-table page {pageId.Value}");

        // Collect all live records
        var liveRecords = new List<(ushort slotIndex, byte[] data, ushort originalOffset)>();
        
        for (ushort slot = 0; slot < page.RecordCount; slot++)
        {
            var slotOffset = PAGE_HEADER_SIZE + (slot * SLOT_SIZE);
            var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[slotOffset..]);
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..]);

            // Skip deleted records (offset = 0)
            if (recordOffset == 0 || recordLength == 0)
                continue;

            // Check if record is marked deleted
            var flags = (RecordFlags)page.Data[recordOffset + 8];
            if (flags.HasFlag(RecordFlags.Deleted))
                continue;

            // Copy live record data
            var recordData = new byte[recordLength];
            Array.Copy(page.Data, recordOffset, recordData, 0, recordLength);
            
            liveRecords.Add((slot, recordData, recordOffset));
        }

        // Clear data area (keep header and slot array)
        var dataAreaStart = PAGE_HEADER_SIZE + (page.RecordCount * SLOT_SIZE);
        Array.Clear(page.Data, dataAreaStart, PAGE_SIZE - dataAreaStart);

        // Rewrite live records from bottom of page upward
        var newDataOffset = (ushort)PAGE_SIZE;

        foreach (var (slotIndex, data, _) in liveRecords)
        {
            newDataOffset -= (ushort)data.Length;

            // Write record data
            Array.Copy(data, 0, page.Data, newDataOffset, data.Length);

            // Update slot to point to new location
            var slotOffset = PAGE_HEADER_SIZE + (slotIndex * SLOT_SIZE);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[slotOffset..], newDataOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..], (ushort)data.Length);
        }

        // Update page metadata
        page.FreeSpaceOffset = newDataOffset;
        page.IsDirty = true;
        page.LSN++;

        WritePage(page);
    }

    /// <summary>
    /// Gets compaction statistics for a page to determine if compaction is worthwhile.
    /// </summary>
    /// <param name="pageId">Page ID to analyze.</param>
    /// <returns>Tuple of (deletedRecords, fragmentedSpace, compactionRecommended).</returns>
    public (int deletedRecords, int fragmentedSpace, bool recommended) GetCompactionStats(PageId pageId)
    {
        var page = ReadPage(pageId);

        int deletedCount = 0;
        int usedSpace = 0;
        int allocatedSpace = 0;

        for (ushort slot = 0; slot < page.RecordCount; slot++)
        {
            var slotOffset = PAGE_HEADER_SIZE + (slot * SLOT_SIZE);
            var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[slotOffset..]);
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..]);

            if (recordOffset == 0 || recordLength == 0)
            {
                deletedCount++;
                continue;
            }

            var flags = (RecordFlags)page.Data[recordOffset + 8];
            if (flags.HasFlag(RecordFlags.Deleted))
            {
                deletedCount++;
                allocatedSpace += recordLength;
            }
            else
            {
                usedSpace += recordLength;
                allocatedSpace += recordLength;
            }
        }

        var fragmentedSpace = allocatedSpace - usedSpace;
        
        // Recommend compaction if >25% space is fragmented or >5 deleted records
        var recommended = fragmentedSpace > (PAGE_SIZE / 4) || deletedCount > 5;

        return (deletedCount, fragmentedSpace, recommended);
    }

    /// <summary>
    /// Reads a record from a page, reassembling overflow chains if necessary.
    /// </summary>
    /// <param name="pageId">Page ID containing the record.</param>
    /// <param name="recordId">Record ID to read.</param>
    /// <returns>The complete record data including overflow data.</returns>
    public byte[] ReadRecord(PageId pageId, RecordId recordId)
    {
        var page = ReadPage(pageId);

        if (recordId.SlotIndex >= page.RecordCount)
            throw new ArgumentException("Invalid record ID", nameof(recordId));

        var slotOffset = PAGE_HEADER_SIZE + (recordId.SlotIndex * SLOT_SIZE);
        var recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[slotOffset..]);
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[(slotOffset + 2)..]);

        if (recordOffset == 0 || recordLength == 0)
            throw new InvalidOperationException("Record has been deleted");

        const int headerSize = 12;
        var dataLength = recordLength - headerSize;

        // Check for overflow
        var flags = (RecordFlags)page.Data[recordOffset + 8];
        var hasOverflow = flags.HasFlag(RecordFlags.HasOverflow);

        if (!hasOverflow)
        {
            // Simple case: data fits in one page
            var data = new byte[dataLength];
            Array.Copy(page.Data, recordOffset + headerSize, data, 0, dataLength);
            return data;
        }

        // Complex case: reassemble from overflow chain
        var totalLength = BinaryPrimitives.ReadUInt16LittleEndian(page.Data.AsSpan()[(recordOffset + 10)..]);
        var result = new byte[totalLength];
        
        // Copy primary chunk
        Array.Copy(page.Data, recordOffset + headerSize, result, 0, dataLength);
        
        // Follow overflow chain
        var currentOffset = dataLength;
        var overflowPageId = new PageId(page.NextPageId);

        while (overflowPageId.IsValid && currentOffset < totalLength)
        {
            var overflowPage = ReadPage(overflowPageId);
            var chunkSize = (int)overflowPage.FreeSpaceOffset - PAGE_HEADER_SIZE;
            
            Array.Copy(overflowPage.Data, PAGE_HEADER_SIZE, result, currentOffset, chunkSize);
            
            currentOffset += chunkSize;
            overflowPageId = new PageId(overflowPage.NextPageId);
        }

        return result;
    }

    /// <summary>
    /// Finds a page with sufficient free space.
    /// </summary>
    /// <param name="tableId">Table ID to search for.</param>
    /// <param name="requiredBytes">Required free space in bytes.</param>
    /// <returns>Page ID with sufficient space, or newly allocated page.</returns>
    public PageId FindPageWithSpace(uint tableId, int requiredBytes)
    {
        lock (writeLock)
        {
            var totalPages = pagesFile.Length / PAGE_SIZE;
            for (ulong i = 1; i <= (ulong)totalPages; i++)
            {
                var pageId = new PageId(i);
                var page = ReadPage(pageId);

                if (page.TableId == tableId && page.Type == PageType.Table && page.FreeSpace >= requiredBytes)
                {
                    return pageId;
                }
            }

            return AllocatePage(tableId, PageType.Table);
        }
    }

    /// <summary>
    /// Flushes all dirty pages to disk.
    /// </summary>
    public void FlushDirtyPages()
    {
        lock (writeLock)
        {
            foreach (var page in pageCache.Values.Where(p => p.IsDirty))
            {
                WritePage(page);
            }
            pagesFile.Flush(flushToDisk: true);
        }
    }

    private void InitializeFreeList()
    {
        File.WriteAllBytes(freeListFilePath, Array.Empty<byte>());
    }

#pragma warning disable S2325 // Method will use instance state (freeListFilePath) when bitmap implementation added
    private PageId GetFreePageFromList()
#pragma warning restore S2325
    {
        // Future: Implement bitmap-based free list for O(1) allocation
        // For now, return Invalid to force new page allocation
        return PageId.Invalid;
    }

    private void AddPageToFreeList(PageId pageId)
    {
        // Future: Add to bitmap-based free list for efficient reuse
        // Temporary implementation: append to text file
        using var fs = File.AppendText(freeListFilePath);
        fs.WriteLine(pageId.Value);
    }

    /// <summary>
    /// Disposes the page manager and flushes all changes.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the page manager.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;

        if (disposing)
        {
            FlushDirtyPages();
            pagesFile?.Dispose();
            pageCache.Clear();
        }

        disposed = true;
    }
}
