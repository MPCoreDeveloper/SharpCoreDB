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
            InvalidateReadEncryption(path);
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

        InvalidateReadEncryption(path);
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

        byte[] fileData;
        // B7: open with FileShare.ReadWrite so the cached write handle (in-place overwrites) can
        // coexist with full-file reads. File.ReadAllBytes defaults to FileShare.Read, which fails
        // while a write handle is open.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            fileData = new byte[fs.Length];
            fs.ReadExactly(fileData);
        }

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

        // Legacy single-blob whole-file encryption (meta.dat, .salt, etc.). The verdict is memoised:
        // probing by "decrypt and catch" costs a full-size allocation plus an exception, and for a
        // plaintext file that used to be the price of every single read.
        if (!ShouldAttemptLegacyDecrypt(path))
        {
            return fileData;
        }

        try
        {
            byte[] plain = this.crypto.Decrypt(this.key, fileData);
            RememberLegacyEncryption(path, encrypted: true);
            return plain;
        }
        catch
        {
            // Legacy plaintext file (no header, not encrypted) — return raw bytes, and remember the
            // answer so the next read does not pay for the probe again.
            RememberLegacyEncryption(path, encrypted: false);
            return fileData;
        }
    }

    /// <summary>
    /// Decrypts every record in an encrypted per-record table file (magic header present)
    /// and rejoins them into a plaintext length-prefixed buffer. Returns null if the file
    /// doesn't exist or no complete records could be read.
    /// </summary>
    private byte[]? DecryptTableFileToPlaintext(string path) => DecryptTableFileToPlaintext(path, out _);

    /// <summary>
    /// Same as <see cref="DecryptTableFileToPlaintext(string)"/>, additionally reporting the PHYSICAL
    /// file offset of each record in the rejoined buffer (index i = the i-th record in walk order).
    /// Scan callers need that map because the rejoined buffer's offsets are not the file's, while the
    /// PK index stores physical offsets.
    /// </summary>
    private byte[]? DecryptTableFileToPlaintext(string path, out long[]? physicalOffsets)
    {
        physicalOffsets = null;

        var records = ReadAllRecords(path)?.ToList();
        if (records is null || records.Count == 0)
        {
            return null;
        }

        var offsets = new long[records.Count];
        using var ms = new MemoryStream();
        Span<byte> lengthBuffer = stackalloc byte[4];
        for (int i = 0; i < records.Count; i++)
        {
            offsets[i] = records[i].RecordOffset;
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, records[i].Data.Length);
            ms.Write(lengthBuffer);
            ms.Write(records[i].Data);
        }

        physicalOffsets = offsets;
        return ms.ToArray();
    }

    /// <inheritdoc />
    public byte[]? ReadBytesWithRecordOffsets(string path, bool noEncrypt, out long[]? physicalOffsets)
    {
        physicalOffsets = null;

        if (!File.Exists(path))
        {
            return null;
        }

        // Plaintext (and every legacy layout): the buffer IS the file, so the buffer offset is already
        // the physical offset and the caller needs no map. Gated on the magic header (not on
        // AreRecordsEncrypted) to stay recursion-free with ReadBytes.
        if (noEncrypt || this.noEncryption || !FileHasEncryptedHeader(path))
        {
            return ReadBytes(path, noEncrypt);
        }

        return DecryptTableFileToPlaintext(path, out physicalOffsets);
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        // ✅ CRITICAL FIX: Check if in transaction - if so, buffer the write!
        if (IsInTransaction)
        {
            var dataToWrite = this.noEncryption ? data : this.crypto.Encrypt(this.key, data);
            InvalidateReadEncryption(path);
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
        
        // Invalidate all cached pages for this file, and re-decide what the file is on the next read.
        InvalidateReadEncryption(path);
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
