// <copyright file="Storage.Advanced.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

/// <summary>
/// Storage implementation - Advanced partial class.
/// Contains SIMD-accelerated operations and advanced diagnostics.
/// </summary>
public partial class Storage
{
    /// <summary>
    /// Scans a page for a specific byte pattern using SIMD acceleration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<long> ScanForPattern(string path, byte pattern)
    {
        var positions = new List<long>();
        
        if (!File.Exists(path))
            return positions;

        const int chunkSize = 64 * 1024;
        byte[]? pooledBuffer = null;
        
        try
        {
            pooledBuffer = this.bufferPool.Rent(chunkSize);
            
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.SequentialScan);
            long filePosition = 0;
            
            while (true)
            {
                int bytesRead = fs.Read(pooledBuffer, 0, chunkSize);
                if (bytesRead == 0)
                    break;

                int index = 0;
                while ((index = SimdHelper.IndexOf(pooledBuffer.AsSpan(index, bytesRead - index), pattern)) != -1)
                {
                    positions.Add(filePosition + index);
                    index++;
                }

                filePosition += bytesRead;
            }
            
            return positions;
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
    /// Validates page integrity using SIMD-accelerated checksums.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool ValidatePageIntegrity(ReadOnlySpan<byte> pageData, int expectedChecksum)
    {
        if (pageData.IsEmpty)
            return false;

        int actualChecksum = SimdHelper.ComputeHashCode(pageData);
        return actualChecksum == expectedChecksum;
    }

    /// <summary>
    /// Compares two pages for equality using SIMD acceleration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool ComparePagesSimd(ReadOnlySpan<byte> page1, ReadOnlySpan<byte> page2)
    {
        return SimdHelper.SequenceEqual(page1, page2);
    }

    /// <summary>
    /// Zeros a page buffer using SIMD acceleration for security.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void SecureZeroPage(Span<byte> pageBuffer)
    {
        SimdHelper.ZeroBuffer(pageBuffer);
    }
}
