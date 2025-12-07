// <copyright file="Storage.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Core.File;
using SharpCoreDB.Interfaces;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;

/// <summary>
/// Implementation of IStorage using encrypted files and memory-mapped files for performance.
/// </summary>
public class Storage(ICryptoService crypto, byte[] key, DatabaseConfig? config = null) : IStorage
{
    private readonly ICryptoService crypto = crypto;
    private readonly byte[] key = key;
    private readonly bool noEncryption = config?.NoEncryptMode ?? false;
    private readonly bool useMemoryMapping = config?.UseMemoryMapping ?? true;

    /// <inheritdoc />
    public void Write(string path, string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
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
    public string? ReadMemoryMapped(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
        using var accessor = mmf.CreateViewAccessor();
        var length = accessor.Capacity;
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, (int)length);
        if (this.noEncryption)
        {
            return Encoding.UTF8.GetString(buffer);
        }
        else
        {
            var plain = this.crypto.Decrypt(this.key, buffer);
            return Encoding.UTF8.GetString(plain);
        }
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, data);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, data);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Use memory-mapped file handler for improved performance on large files
        byte[] fileData;
        // Temporarily disable memory mapping to debug
        // if (this.useMemoryMapping)
        // {
        //     using var handler = MemoryMappedFileHandler.TryCreate(path, this.useMemoryMapping);
        //     if (handler != null && handler.IsMemoryMapped)
        //     {
        //         fileData = handler.ReadAllBytes();
        //     }
        //     else
        //     {
        //         // Fallback to traditional file reading
        //         fileData = File.ReadAllBytes(path);
        //     }
        // }
        // else
        // {
            fileData = File.ReadAllBytes(path);
        // }

        if (this.noEncryption)
        {
            return fileData;
        }
        else
        {
            try
            {
                return this.crypto.Decrypt(this.key, fileData);
            }
            catch
            {
                // Assume plain data (for appended inserts)
                return fileData;
            }
        }
    }

    /// <inheritdoc />
    public long AppendBytes(string path, byte[] data)
    {
        // PERFORMANCE: True append for O(1) inserts - no compression/encryption for speed
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        long position = fs.Position;
        using var writer = new BinaryWriter(fs);
        writer.Write(data.Length);
        writer.Write(data);
        return position;
    }

    /// <inheritdoc />
    public byte[]? ReadBytesFrom(string path, long offset)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // PERFORMANCE: Read from offset for position-based access
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        using var reader = new BinaryReader(fs);
        int length = reader.ReadInt32();
        return reader.ReadBytes(length);
    }
}
