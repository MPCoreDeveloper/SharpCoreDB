// <copyright file="Storage.PageCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// Storage implementation - PageCache partial class.
/// Handles page-level caching for improved read performance.
/// </summary>
public partial class Storage
{
    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? ReadBytesAt(string path, long position, int maxLength, bool noEncrypt)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        int pageId = ComputePageId(path, position);

        // Try page cache first if enabled
        if (this.pageCache != null && maxLength <= this.pageSize)
        {
            var cachedPage = this.pageCache.GetPage(pageId, (id) =>
            {
                return LoadPageFromDisk(path, position, maxLength, noEncrypt);
            });

            try
            {
                int offsetInPage = (int)(position % this.pageSize);
                int bytesToCopy = Math.Min(maxLength, this.pageSize - offsetInPage);
                
                var result = new byte[bytesToCopy];
                cachedPage.Buffer.Slice(offsetInPage, bytesToCopy).CopyTo(result);
                
                return result;
            }
            finally
            {
                this.pageCache.UnpinPage(pageId);
            }
        }

        return ReadBytesAtDirect(path, position, maxLength, noEncrypt);
    }

    /// <inheritdoc />
    public byte[]? ReadBytesAt(string path, long position, int maxLength)
    {
        return ReadBytesAt(path, position, maxLength, false);
    }

    /// <summary>
    /// Loads a page from disk into a byte array for caching.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ReadOnlySpan<byte> LoadPageFromDisk(string path, long position, int maxLength, bool noEncrypt)
    {
        var data = ReadBytesAtDirect(path, position, maxLength, noEncrypt);
        if (data == null)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        if (data.Length > this.pageSize)
        {
            Array.Resize(ref data, this.pageSize);
        }

        return new ReadOnlySpan<byte>(data);
    }

    /// <summary>
    /// Direct read from disk without caching.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private byte[]? ReadBytesAtDirect(string path, long position, int maxLength, bool noEncrypt)
    {
        byte[]? pooledBuffer = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            fs.Seek(position, SeekOrigin.Begin);
            
            pooledBuffer = this.bufferPool.Rent(maxLength);
            Span<byte> bufferSpan = pooledBuffer.AsSpan(0, maxLength);
            
            int bytesRead = fs.Read(bufferSpan);
            if (bytesRead == 0)
            {
                return null;
            }

            var effectiveNoEncrypt = noEncrypt || this.noEncryption;
            
            byte[] result;
            if (effectiveNoEncrypt)
            {
                result = new byte[bytesRead];
                bufferSpan[..bytesRead].CopyTo(result);
            }
            else
            {
                try
                {
                    var dataToDecrypt = new byte[bytesRead];
                    bufferSpan[..bytesRead].CopyTo(dataToDecrypt);
                    result = this.crypto.Decrypt(this.key, dataToDecrypt);
                }
                catch
                {
                    result = new byte[bytesRead];
                    bufferSpan[..bytesRead].CopyTo(result);
                }
            }
            
            return result;
        }
        finally
        {
            if (pooledBuffer != null)
            {
                this.bufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Gets page cache diagnostics.
    /// </summary>
    public string GetPageCacheDiagnostics()
    {
        return this.pageCache?.GetDiagnostics() ?? "PageCache not enabled";
    }
}
