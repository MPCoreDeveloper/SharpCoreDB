// <copyright file="PageManager.FreePageBitmap.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System.Collections;

/// <summary>
/// PageManager partial class - Free page bitmap implementation.
/// ✅ OPTIMIZED: O(1) free page lookup using bitmap
/// </summary>
public partial class PageManager
{
    /// <summary>
    /// Bitmap-based free page tracker for O(1) lookup.
    /// Uses a BitArray to track allocated (1) vs free (0) pages.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="FreePageBitmap"/> class.
    /// </remarks>
    /// <param name="maxPages">Maximum number of pages to track.</param>
    private sealed class FreePageBitmap(int maxPages)
    {
        private readonly BitArray bitmap = new (maxPages);
        private readonly Lock bitmapLock = new();
        private readonly int maxPages = maxPages;

        /// <summary>
        /// Marks a page as allocated.
        /// </summary>
        /// <param name="pageId">Page ID to mark as allocated.</param>
        public void MarkAllocated(ulong pageId)
        {
            if (pageId >= (ulong)maxPages)
                return; // Out of range

            lock (bitmapLock)
            {
                bitmap[(int)pageId] = true;
            }
        }

        /// <summary>
        /// Marks a page as free.
        /// </summary>
        /// <param name="pageId">Page ID to mark as free.</param>
        public void MarkFree(ulong pageId)
        {
            if (pageId >= (ulong)maxPages)
                return; // Out of range

            lock (bitmapLock)
            {
                bitmap[(int)pageId] = false;
            }
        }

        /// <summary>
        /// Checks if a page is allocated.
        /// </summary>
        /// <param name="pageId">Page ID to check.</param>
        /// <returns>True if page is allocated, false if free.</returns>
        public bool IsAllocated(ulong pageId)
        {
            if (pageId >= (ulong)maxPages)
                return true; // Out of range = assume allocated

            lock (bitmapLock)
            {
                return bitmap[(int)pageId];
            }
        }

        /// <summary>
        /// Finds the first free page starting from a given page ID.
        /// Reserved for future use in space optimization.
        /// </summary>
        /// <param name="startPageId">Starting page ID for search.</param>
        /// <returns>First free page ID, or 0 if none found.</returns>
#pragma warning disable S1144 // Reserved for future use
        public ulong FindFirstFreePage(ulong startPageId)
#pragma warning restore S1144
        {
            lock (bitmapLock)
            {
                for (ulong i = startPageId; i < (ulong)maxPages; i++)
                {
                    if (!bitmap[(int)i])
                        return i;
                }
                return 0; // No free pages found
            }
        }
    }
}
