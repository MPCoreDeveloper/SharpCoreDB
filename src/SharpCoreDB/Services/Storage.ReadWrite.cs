// <copyright file="Storage.ReadWrite.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;

/// <summary>
/// Storage implementation - Read/Write partial class.
/// Handles basic file read and write operations with encryption support.
/// </summary>
public partial class Storage
{
    /// <inheritdoc />
    public void Write(string path, string data)
    {
        path = Path.GetFullPath(path);
        var plain = Encoding.UTF8.GetBytes(data);
        
        // ✅ CRITICAL FIX: Check if in transaction - if so, buffer the write!
        if (IsInTransaction)
        {
            var dataToWrite = this.noEncryption ? plain : this.crypto.Encrypt(this.key, plain);
            this.transactionBuffer.BufferWrite(path, dataToWrite);
            return;
        }
        
        // Normal write (not in transaction)
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, plain);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, plain);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public string? Read(string path)
    {
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            return null;
        }

        var data = File.ReadAllBytes(path);
        try
        {
            if (this.noEncryption)
            {
                return Encoding.UTF8.GetString(data);
            }
            else
            {
                var plain = this.crypto.Decrypt(this.key, data);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        return ReadBytes(path, false);
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path, bool noEncrypt)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] fileData = File.ReadAllBytes(path);

        var effectiveNoEncrypt = noEncrypt || this.noEncryption;
        if (effectiveNoEncrypt)
        {
            return fileData;
        }

        // ✅ Known Issue 1 FIX (opt-in): table data files carrying the encrypted per-record magic
        // header store each record as AES-256-GCM ciphertext. Decrypt every record and rejoin
        // them into a plaintext length-prefixed buffer so existing full-table scan/compaction
        // parsers work unchanged. This is gated on UseRecordEncryption (config flag) — when the
        // flag is off, behavior is byte-for-byte identical to the original engine.
        if (FileHasEncryptedHeader(path))
        {
            return DecryptTableFileToPlaintext(path) ?? fileData;
        }

        // Legacy single-blob whole-file encryption (meta.dat, .salt, etc.)
        try
        {
            return this.crypto.Decrypt(this.key, fileData);
        }
        catch
        {
            // Legacy plaintext file (no header, not encrypted) — return raw bytes.
            return fileData;
        }
    }

    /// <summary>
    /// Decrypts every record in an encrypted per-record table file (magic header present)
    /// and rejoins them into a plaintext length-prefixed buffer. Returns null if the file
    /// doesn't exist or no complete records could be read.
    /// </summary>
    private byte[]? DecryptTableFileToPlaintext(string path)
    {
        var records = ReadAllRecords(path)?.ToList();
        if (records is null || records.Count == 0)
        {
            return null;
        }

        using var ms = new MemoryStream();
        Span<byte> lengthBuffer = stackalloc byte[4];
        foreach (var (_, data) in records)
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, data.Length);
            ms.Write(lengthBuffer);
            ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        // ✅ CRITICAL FIX: Check if in transaction - if so, buffer the write!
        if (IsInTransaction)
        {
            var dataToWrite = this.noEncryption ? data : this.crypto.Encrypt(this.key, data);
            this.transactionBuffer.BufferWrite(path, dataToWrite);
            return;
        }
        
        // Normal write (not in transaction)
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, data);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, data);
            File.WriteAllBytes(path, encrypted);
        }
        
        // Invalidate all cached pages for this file
        if (this.pageCache != null)
        {
            this.pageCache.Clear(flushDirty: false);
        }
    }

    /// <inheritdoc />
    public string? ReadMemoryMapped(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[]? pooledBuffer = null;
        try
        {
            using var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(path, FileMode.Open);
            using var accessor = mmf.CreateViewAccessor();
            var length = accessor.Capacity;
            
            pooledBuffer = this.bufferPool.Rent((int)length);
            accessor.ReadArray(0, pooledBuffer, 0, (int)length);
            
            ReadOnlySpan<byte> dataSpan = pooledBuffer.AsSpan(0, (int)length);
            
            if (this.noEncryption)
            {
                return Encoding.UTF8.GetString(dataSpan);
            }
            else
            {
                var dataArray = dataSpan.ToArray();
                var plain = this.crypto.Decrypt(this.key, dataArray);
                return Encoding.UTF8.GetString(plain);
            }
        }
        finally
        {
            if (pooledBuffer != null)
            {
                this.bufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }
}
